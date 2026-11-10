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
        var staging = Path.Combine(Path.GetTempPath(), "StarTruckerMP", $"update-{update.Number}");

        if (Directory.Exists(staging))
            Directory.Delete(staging, true);

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
            BuildScript(payloadDirectory, installerPath, appDirectory, staging),
            new UTF8Encoding(false));

        var startInfo = new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = staging
        };

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException(Strings.UpdateHelperFailed);
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
        string staging)
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        script.AppendLine($"Wait-Process -Id {Environment.ProcessId} -Timeout 120");
        script.AppendLine("Start-Sleep -Milliseconds 700");

        if (payloadDirectory is not null)
        {
            script.AppendLine(
                $"Copy-Item -Path '{Quote(payloadDirectory)}\\*' -Destination '{Quote(appDirectory)}' -Recurse -Force");
        }
        else
        {
            var arguments = $"{InstallerArguments} /DIR=\"{appDirectory}\"";
            script.AppendLine(
                $"Start-Process -FilePath '{Quote(installerPath!)}' -ArgumentList '{Quote(arguments)}' -Wait");
        }

        script.AppendLine($"Start-Process -FilePath '{Quote(Path.Combine(appDirectory, LauncherExeName))}'");
        script.AppendLine("Start-Sleep -Seconds 3");
        script.AppendLine($"Remove-Item -LiteralPath '{Quote(staging)}' -Recurse -Force");
        script.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force");

        return script.ToString();
    }

    /// <summary>Escapes a path for a single-quoted PowerShell string.</summary>
    private static string Quote(string value) => value.Replace("'", "''");
}
