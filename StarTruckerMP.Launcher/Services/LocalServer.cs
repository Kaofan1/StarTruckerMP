using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarTruckerMP.Launcher.Localization;

namespace StarTruckerMP.Launcher.Services;

internal enum LocalServerState
{
    Missing,
    Stopped,
    Running
}

internal sealed record LocalServerStatus(LocalServerState State, string? ExecutablePath, int ProcessId);

/// <summary>
/// Starts the multiplayer server on this PC and keeps the installed copy in step with the
/// newest release. The client authenticates against 127.0.0.1:7777, so without a server the
/// mod refuses the connection before the overlay even appears. The executable comes from an
/// optional <c>payload\server</c> folder shipped next to the launcher, or from the server
/// package on GitHub when that folder is empty or outdated.
/// </summary>
internal static class LocalServer
{
    public const string ProcessName = "StarTruckerMP.Server";
    public const string ExecutableName = "StarTruckerMP.Server.exe";

    /// <summary>Release tag of the installed package, so a newer release can replace it.</summary>
    private const string InstalledTagFileName = "package-tag.txt";

    /// <summary>Server files live in the launcher data folder, never in the game folder.</summary>
    public static string InstallDirectory => Path.Combine(SettingsStore.DataDirectory, "server");

    public static string InstalledExecutable => Path.Combine(InstallDirectory, ExecutableName);

    public static string PayloadDirectory => Path.Combine(AppContext.BaseDirectory, "payload", "server");

    private static string InstalledTagPath => Path.Combine(InstallDirectory, InstalledTagFileName);

    public static LocalServerStatus Detect()
    {
        var running = FindRunning();
        if (running is not null)
            return new LocalServerStatus(LocalServerState.Running, InstalledExecutable, running.Id);

        if (File.Exists(InstalledExecutable))
            return new LocalServerStatus(LocalServerState.Stopped, InstalledExecutable, 0);

        var payloadExecutable = Path.Combine(PayloadDirectory, ExecutableName);
        if (File.Exists(payloadExecutable))
            return new LocalServerStatus(LocalServerState.Stopped, payloadExecutable, 0);

        return new LocalServerStatus(LocalServerState.Missing, null, 0);
    }

    /// <summary>
    /// Makes sure the files are unpacked and launches the server. A server that dies within
    /// a second usually means the port is taken, so the caller gets that hint instead of
    /// a silent no-op.
    /// </summary>
    public static async Task<LocalServerStatus> StartAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var executable = await EnsureInstalledAsync(progress, cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(executable)!;
        Directory.CreateDirectory(directory);

        var process = Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException(Strings.LocalServerStartFailed(Strings.LocalServerPortHint));

        await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
        process.Refresh();

        if (process.HasExited)
            throw new InvalidOperationException(Strings.LocalServerStartFailed(Strings.LocalServerPortHint));

        return new LocalServerStatus(LocalServerState.Running, executable, process.Id);
    }

    public static bool Stop()
    {
        var process = FindRunning();
        if (process is null)
            return false;

        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces the installed server with the newest release package when the tag changed.
    /// Returns the new tag, or null when the copy is current, missing or in use by a running
    /// server. Callers can log the tag to show the server was refreshed.
    /// </summary>
    public static async Task<string?> UpdateIfOutdatedAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(InstalledExecutable))
            return null;

        // Windows keeps the executable locked while the server runs, so the update waits for
        // the next start instead of failing under the user's feet.
        if (FindRunning() is not null)
            return null;

        var release = await GitHubReleases
            .FindLatestAssetAsync(ModInstaller.RepositoryOwner, ModInstaller.RepositoryName, IsServerAsset, cancellationToken)
            .ConfigureAwait(false);

        if (release is null)
            return null;

        if (string.Equals(ReadInstalledTag(), release.Tag, StringComparison.OrdinalIgnoreCase))
            return null;

        await InstallReleaseAsync(release, progress, cancellationToken).ConfigureAwait(false);
        WriteInstalledTag(release.Tag);
        return release.Tag;
    }

    private static Process? FindRunning()
    {
        try
        {
            return Process.GetProcessesByName(ProcessName).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> EnsureInstalledAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(InstalledExecutable))
        {
            // A newer release replaces the installed copy; without one the server still starts.
            await UpdateIfOutdatedAsync(progress, cancellationToken).ConfigureAwait(false);
            return InstalledExecutable;
        }

        var payloadExecutable = Path.Combine(PayloadDirectory, ExecutableName);
        if (File.Exists(payloadExecutable))
        {
            progress?.Report(Strings.LocalServerCopying(PayloadDirectory));

            try
            {
                CopyDirectory(PayloadDirectory, InstallDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(Strings.LocalServerInstallFailed(exception.Message), exception);
            }

            if (File.Exists(InstalledExecutable))
            {
                WriteInstalledTag("payload");
                return InstalledExecutable;
            }
        }

        progress?.Report(Strings.LocalServerLookingUp);

        var release = await GitHubReleases
            .FindLatestAssetAsync(ModInstaller.RepositoryOwner, ModInstaller.RepositoryName, IsServerAsset, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(Strings.LocalServerPackageMissing);

        await InstallReleaseAsync(release, progress, cancellationToken).ConfigureAwait(false);
        WriteInstalledTag(release.Tag);

        if (!File.Exists(InstalledExecutable))
            throw new InvalidOperationException(Strings.LocalServerPackageMissing);

        return InstalledExecutable;
    }

    /// <summary>
    /// Downloads the asset and copies it over the install folder. The package holds only build
    /// output, so <c>server.json</c>, the certificate and the logs survive an update.
    /// </summary>
    private static async Task InstallReleaseAsync(
        ReleaseAsset release,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(Strings.LocalServerDownloading(release.Name));

        var archive = await Downloader.DownloadToTempAsync(release.Url, progress, cancellationToken)
            .ConfigureAwait(false);

        var unpacked = Path.Combine(Path.GetTempPath(), "StarTruckerMP", $"server-{Guid.NewGuid():N}");

        try
        {
            progress?.Report(Strings.LocalServerExtracting);
            ZipFile.ExtractToDirectory(archive, unpacked, overwriteFiles: true);
            Directory.CreateDirectory(InstallDirectory);
            CopyDirectory(unpacked, InstallDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidOperationException(Strings.LocalServerInstallFailed(exception.Message), exception);
        }
        finally
        {
            try
            {
                File.Delete(archive);
            }
            catch
            {
                // Temp files are cleaned by the OS.
            }

            try
            {
                Directory.Delete(unpacked, true);
            }
            catch
            {
                // Temp folders are cleaned by the OS.
            }
        }
    }

    private static string? ReadInstalledTag()
    {
        try
        {
            return File.Exists(InstalledTagPath) ? File.ReadAllText(InstalledTagPath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteInstalledTag(string tag)
    {
        try
        {
            Directory.CreateDirectory(InstallDirectory);
            File.WriteAllText(InstalledTagPath, tag);
        }
        catch
        {
            // The tag only drives the next update; a failed write must not break the start.
        }
    }

    /// <summary>The server zip is the Windows build, never the Linux or Docker one.</summary>
    private static bool IsServerAsset(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("server", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("win", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("docker", StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
