using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarTruckerMP.Launcher.Localization;

namespace StarTruckerMP.Launcher.Services;

/// <summary>
/// Downloads a newer launcher build and hands the file swap to a short PowerShell
/// helper, because the running executable cannot replace itself. The helper waits
/// for this process to exit, copies the staged files over the launcher folder and
/// starts the new build, so the window never has to be closed by hand.
/// Every attempt uses its own staging folder: a helper or an antivirus scan can keep
/// the previous one open, and reusing it made the next update fail with "being used
/// by another process". The helper logs what it does to
/// <c>%TEMP%\StarTruckerMP\apply-update.log</c>, because a silent copy hides failures.
/// </summary>
internal static class LauncherUpdater
{
    private const string LauncherExeName = "StarTruckerMP.Launcher.exe";

    private const string InstallerArguments =
        "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

    /// <summary>Folder of the running launcher (single-file publish keeps the exe alone there).</summary>
    public static string AppDirectory
    {
        get
        {
            var processPath = Environment.ProcessPath;
            var directory = string.IsNullOrEmpty(processPath) ? null : Path.GetDirectoryName(processPath);

            return string.IsNullOrEmpty(directory) ? AppContext.BaseDirectory : directory;
        }
    }

    /// <summary>Where the update helper writes its log, kept outside the staging folder.</summary>
    public static string HelperLogPath => Path.Combine(StagingRoot, "apply-update.log");

    private static string StagingRoot => Path.Combine(Path.GetTempPath(), "StarTruckerMP");

    /// <summary>
    /// Downloads the update into a temporary folder and starts the helper that applies it
    /// once this process has exited. Throws when nothing could be prepared.
    /// </summary>
    public static async Task StageAsync(
        LauncherUpdate update,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var appDirectory = AppDirectory;

        Directory.CreateDirectory(StagingRoot);
        CleanupOldStaging(update.Number);

        var staging = Path.Combine(
            StagingRoot,
            $"update-{update.Number}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(staging);

        string? payloadDirectory = null;
        string? installerPath = null;

        // A portable zip is the cleanest upgrade: it contains exactly the launcher files
        // and is copied over the same folder. It needs a writable folder, though, so a
        // machine-wide install falls back to the installer.
        if (!string.IsNullOrWhiteSpace(update.PortableUrl) && IsDirectoryWritable(appDirectory))
        {
            progress?.Report(Strings.UpdateDownloading(update.Name));

            var archive = await Downloader.DownloadToTempAsync(update.PortableUrl!, progress, cancellationToken)
                .ConfigureAwait(false);

            var extracted = Path.Combine(staging, "files");
            ZipFile.ExtractToDirectory(archive, extracted, true);
            File.Delete(archive);

            payloadDirectory = FindLauncherDirectory(extracted, 0);
        }

        if (payloadDirectory is null)
        {
            if (string.IsNullOrWhiteSpace(update.InstallerUrl))
                throw new InvalidOperationException(Strings.UpdateAssetMissing);

            progress?.Report(Strings.UpdateDownloading(update.Name));

            var archive = await Downloader.DownloadToTempAsync(update.InstallerUrl!, progress, cancellationToken)
                .ConfigureAwait(false);

            installerPath = Path.Combine(staging, "StarTruckerMP-Setup.exe");
            File.Move(archive, installerPath, true);
        }

        var scriptPath = Path.Combine(staging, "apply-update.ps1");
        File.WriteAllText(
            scriptPath,
            BuildScript(payloadDirectory, installerPath, appDirectory, staging, HelperLogPath),
            new UTF8Encoding(false));

        var startInfo = new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,

            // Never the staging folder itself: a process working directory keeps the
            // folder locked, which is what blocked the next update attempt.
            WorkingDirectory = StagingRoot
        };

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException(Strings.UpdateHelperFailed);
    }

    /// <summary>Removes staging folders of earlier attempts, when nothing holds them any more.</summary>
    private static void CleanupOldStaging(int number)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(StagingRoot, $"update-{number}*"))
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch
                {
                    // A helper or a virus scan may still hold it; it is temporary data.
                }
            }
        }
        catch
        {
            // Enumerating the temp folder can fail; updating still works without cleanup.
        }
    }

    private static string? FindLauncherDirectory(string directory, int depth)
    {
        if (File.Exists(Path.Combine(directory, LauncherExeName)))
            return directory;

        // Compress-Archive writes the files at the root, but a hand-made zip may wrap
        // everything in a single folder, so look one level down as well.
        if (depth >= 2)
            return null;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var found = FindLauncherDirectory(child, depth + 1);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".update-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildScript(
        string? payloadDirectory,
        string? installerPath,
        string appDirectory,
        string staging,
        string logPath)
    {
        var launcherPath = Path.Combine(appDirectory, LauncherExeName);
        var installerArguments = $"{InstallerArguments} /DIR=\"{appDirectory}\"";
        var script = new StringBuilder();

        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$log = '{Quote(logPath)}'");
        script.AppendLine(
            "function Write-UpdateLog([string] $message) { try { Add-Content -LiteralPath $log " +
            "-Value ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $message) } catch { } }");
        script.AppendLine(
            $"Write-UpdateLog 'helper started, waiting for launcher {Environment.ProcessId}'");
        script.AppendLine(
            $"try {{ Wait-Process -Id {Environment.ProcessId} -Timeout 120 -ErrorAction SilentlyContinue }} catch {{ }}");
        script.AppendLine("Start-Sleep -Milliseconds 800");

        if (payloadDirectory is not null)
        {
            // The exe can stay locked for a moment after the process exits, and a freshly
            // downloaded file may be scanned first, so the copy is retried instead of
            // failing the whole update on the first attempt.
            script.AppendLine("$applied = $false");
            script.AppendLine("for ($attempt = 1; $attempt -le 10 -and -not $applied; $attempt++) {");
            script.AppendLine("    try {");
            script.AppendLine(
                $"        Copy-Item -Path '{Quote(payloadDirectory)}\\*' -Destination '{Quote(appDirectory)}' " +
                "-Recurse -Force -ErrorAction Stop");
            script.AppendLine("        $applied = $true");
            script.AppendLine("        Write-UpdateLog ('payload copied on attempt ' + $attempt)");
            script.AppendLine("    } catch {");
            script.AppendLine(
                "        Write-UpdateLog ('copy attempt ' + $attempt + ' failed: ' + $_.Exception.Message)");
            script.AppendLine("        Start-Sleep -Seconds 2");
            script.AppendLine("    }");
            script.AppendLine("}");

            if (installerPath is not null)
            {
                script.AppendLine("if (-not $applied) {");
                script.AppendLine("    Write-UpdateLog 'copy failed, running the installer'");
                script.AppendLine("    try {");
                script.AppendLine(
                    $"        Start-Process -FilePath '{Quote(installerPath)}' " +
                    $"-ArgumentList '{Quote(installerArguments)}' -Wait");
                script.AppendLine("        $applied = $true");
                script.AppendLine("        Write-UpdateLog 'installer finished'");
                script.AppendLine("    } catch {");
                script.AppendLine(
                    "        Write-UpdateLog ('installer failed: ' + $_.Exception.Message)");
                script.AppendLine("    }");
                script.AppendLine("}");
            }
        }
        else
        {
            script.AppendLine("Write-UpdateLog 'running the installer'");
            script.AppendLine("try {");
            script.AppendLine(
                $"    Start-Process -FilePath '{Quote(installerPath!)}' " +
                $"-ArgumentList '{Quote(installerArguments)}' -Wait");
            script.AppendLine("    Write-UpdateLog 'installer finished'");
            script.AppendLine("} catch {");
            script.AppendLine("    Write-UpdateLog ('installer failed: ' + $_.Exception.Message)");
            script.AppendLine("}");
        }

        script.AppendLine($"Write-UpdateLog 'starting {Quote(launcherPath)}'");
        script.AppendLine("try {");
        script.AppendLine($"    Start-Process -FilePath '{Quote(launcherPath)}'");
        script.AppendLine("} catch {");
        script.AppendLine("    Write-UpdateLog ('relaunch failed: ' + $_.Exception.Message)");
        script.AppendLine("}");
        script.AppendLine("Start-Sleep -Seconds 2");
        script.AppendLine("try {");
        script.AppendLine(
            $"    Remove-Item -LiteralPath '{Quote(staging)}' -Recurse -Force -ErrorAction Stop");
        script.AppendLine("    Write-UpdateLog 'staging cleaned up'");
        script.AppendLine("} catch {");
        script.AppendLine("    Write-UpdateLog ('staging cleanup failed: ' + $_.Exception.Message)");
        script.AppendLine("}");
        script.AppendLine("try { Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force } catch { }");

        return script.ToString();
    }

    /// <summary>Escapes a path for a single-quoted PowerShell string.</summary>
    private static string Quote(string value) => value.Replace("'", "''");
}
