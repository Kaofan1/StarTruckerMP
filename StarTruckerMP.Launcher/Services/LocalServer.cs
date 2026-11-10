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
/// Starts the multiplayer server on this PC. The client authenticates against
/// 127.0.0.1:7777, so without a server the mod refuses the connection before the overlay
/// even appears. The executable comes from an optional <c>payload\server</c> folder shipped
/// next to the launcher, or from the server package on GitHub when that folder is empty.
/// </summary>
internal static class LocalServer
{
    public const string ProcessName = "StarTruckerMP.Server";
    public const string ExecutableName = "StarTruckerMP.Server.exe";

    /// <summary>Server files live in the launcher data folder, never in the game folder.</summary>
    public static string InstallDirectory => Path.Combine(SettingsStore.DataDirectory, "server");

    public static string InstalledExecutable => Path.Combine(InstallDirectory, ExecutableName);

    public static string PayloadDirectory => Path.Combine(AppContext.BaseDirectory, "payload", "server");

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
            return InstalledExecutable;

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
                return InstalledExecutable;
        }

        progress?.Report(Strings.LocalServerLookingUp);

        var url = await GitHubReleases
            .FindAssetUrlAsync(ModInstaller.RepositoryOwner, ModInstaller.RepositoryName, IsServerAsset, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(Strings.LocalServerPackageMissing);

        var fileName = url[(url.LastIndexOf('/') + 1)..];
        progress?.Report(Strings.LocalServerDownloading(fileName));

        var archive = await Downloader.DownloadToTempAsync(url, progress, cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report(Strings.LocalServerExtracting);
            Directory.CreateDirectory(InstallDirectory);
            ZipFile.ExtractToDirectory(archive, InstallDirectory, overwriteFiles: true);
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
        }

        if (!File.Exists(InstalledExecutable))
            throw new InvalidOperationException(Strings.LocalServerPackageMissing);

        return InstalledExecutable;
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
