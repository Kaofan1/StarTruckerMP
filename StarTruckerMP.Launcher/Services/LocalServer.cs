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

internal sealed record LocalServerStatus(LocalServerState State, string? ExecutablePath, int ProcessId, bool IsService = false);

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

    /// <summary>The service the standalone Windows installer registers.</summary>
    public const string ServiceName = "StarTruckerMPServer";

    /// <summary>Release tag of the installed package, so a newer release can replace it.</summary>
    private const string InstalledTagFileName = "package-tag.txt";

    /// <summary>Status page of the running server, shown on the card so it opens in one click.</summary>
    public const string StatusPageUrl = "https://localhost:7777/";

    /// <summary>Server files live in the launcher data folder, never in the game folder.</summary>
    public static string InstallDirectory => Path.Combine(SettingsStore.DataDirectory, "server");

    public static string InstalledExecutable => Path.Combine(InstallDirectory, ExecutableName);

    /// <summary>
    /// Where the standalone Setup keeps the live settings. The folder is writable for normal
    /// users, so the file can be edited without administrator rights.
    /// </summary>
    public static string SharedConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StarTruckerMP");

    /// <summary>The settings file the server actually reads, or null when none exists yet.</summary>
    public static string? ConfigPath
    {
        get
        {
            var shared = Path.Combine(SharedConfigDirectory, "server.json");
            if (File.Exists(shared))
                return shared;

            var local = Path.Combine(InstallDirectory, "server.json");
            return File.Exists(local) ? local : null;
        }
    }

    /// <summary>
    /// Opens the settings in Notepad, because the default handler for .json is often a browser.
    /// Returns false when the server has not written its settings yet.
    /// </summary>
    public static bool OpenConfig()
    {
        var path = ConfigPath;
        if (path is null)
            return false;

        Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"")
        {
            UseShellExecute = true
        });

        return true;
    }

    public static string PayloadDirectory => Path.Combine(AppContext.BaseDirectory, "payload", "server");

    private static string InstalledTagPath => Path.Combine(InstallDirectory, InstalledTagFileName);

    public static LocalServerStatus Detect()
    {
        var running = FindRunning();
        var service = QueryServiceState();

        // A server installed by StarTruckerMP-Server-Setup.exe runs as a service owned by
        // Windows, not by the launcher, so it is reported separately and managed through sc.
        if (running is not null)
            return new LocalServerStatus(LocalServerState.Running, InstalledExecutable, running.Id, service == WindowsServiceState.Running);

        if (service == WindowsServiceState.Running)
            return new LocalServerStatus(LocalServerState.Running, ServiceExecutablePath() ?? InstalledExecutable, 0, true);

        if (service == WindowsServiceState.Stopped)
            return new LocalServerStatus(LocalServerState.Stopped, ServiceExecutablePath() ?? InstalledExecutable, 0, true);

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

        // No console window: the card shows the status page, and the server output goes to the
        // log files next to it, so nothing disappears when the launcher closes.
        var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            // The .\ prefix matters: cmd does not search the working directory when
            // NoDefaultCurrentDirectoryInExePath is set, which PowerShell hosts do.
            Arguments = $"/c \".\\{ExecutableName} > server-out.log 2> server-err.log\"",
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException(Strings.LocalServerStartFailed(Strings.LocalServerPortHint));

        await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
        process.Refresh();

        if (process.HasExited)
            throw new InvalidOperationException(Strings.LocalServerStartFailed(Strings.LocalServerPortHint));

        // The started process is the shell that keeps the log redirection alive; the card and
        // the stop button work on the server process itself.
        var server = FindRunning();
        return new LocalServerStatus(LocalServerState.Running, executable, server?.Id ?? process.Id);
    }

    public static bool Stop(LocalServerStatus status)
    {
        if (status.IsService)
            return StopService();

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
    /// Starts the installed Windows service. Windows refuses with access denied unless the
    /// launcher is elevated, so the caller reports that instead of a generic failure.
    /// </summary>
    public static bool StartService()
    {
        if (!RunServiceCommand($"start {ServiceName}", alreadyInState: 1056))
            return false;

        return WaitForService(WindowsServiceState.Running);
    }

    private static bool StopService()
    {
        if (!RunServiceCommand($"stop {ServiceName}", alreadyInState: 1062))
            return false;

        return WaitForService(WindowsServiceState.Stopped);
    }

    /// <summary>
    /// Sends one sc command. Windows answers access denied for a normal user, so the command is
    /// repeated with the UAC prompt instead of sending the operator to services.msc by hand.
    /// </summary>
    private static bool RunServiceCommand(string arguments, int alreadyInState)
    {
        var (exitCode, _) = RunServiceControl(arguments);

        if (exitCode == 5)
            return RunServiceControlElevated(arguments);

        return exitCode == 0 || exitCode == alreadyInState;
    }

    private static bool RunServiceControlElevated(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null)
                return false;

            if (!process.WaitForExit(30000))
                return false;

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The elevation prompt was declined, which is a normal answer.
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForService(WindowsServiceState expected)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(250);
            if (QueryServiceState() == expected)
                return true;
        }

        return QueryServiceState() == expected;
    }

    private enum WindowsServiceState
    {
        NotInstalled,
        Stopped,
        Running
    }

    private static WindowsServiceState QueryServiceState()
    {
        if (!OperatingSystem.IsWindows())
            return WindowsServiceState.NotInstalled;

        var (exitCode, output) = RunServiceControl($"query {ServiceName}");
        if (exitCode != 0)
            return WindowsServiceState.NotInstalled;

        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return WindowsServiceState.Running;

        return output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)
            ? WindowsServiceState.Stopped
            : WindowsServiceState.NotInstalled;
    }

    private static (int ExitCode, string Output) RunServiceControl(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return (-1, string.Empty);

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Nothing to do; the query below already timed out.
                }

                return (-1, output);
            }

            return (process.ExitCode, output);
        }
        catch
        {
            return (-1, string.Empty);
        }
    }

    private static string? ServiceExecutablePath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            if (key?.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
                return null;

            imagePath = imagePath.Trim();
            return imagePath.StartsWith('"') ? imagePath.Trim('"') : imagePath;
        }
        catch
        {
            return null;
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
