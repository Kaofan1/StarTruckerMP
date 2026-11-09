using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace StarTruckMP.Launcher.Services;

/// <summary>
/// Installs the BepInEx IL2CPP pack and the StarTruckMP client plugin into the game folder.
/// Everything is downloaded from public GitHub releases, nothing is bundled with the launcher.
/// </summary>
internal sealed class ModInstaller
{
    public const string RepositoryOwner = "Kaofan1";
    public const string RepositoryName = "StarTruckerMP";

    private readonly LauncherLogger _log;

    public ModInstaller(LauncherLogger log)
    {
        _log = log;
    }

    public static string BepInExCoreDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "core");

    public static string PluginDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "plugins", "StarTruckMP");

    public static bool HasBepInEx(string gamePath) =>
        File.Exists(Path.Combine(gamePath, "winhttp.dll")) &&
        File.Exists(Path.Combine(gamePath, "doorstop_config.ini")) &&
        Directory.Exists(BepInExCoreDirectory(gamePath));

    public static bool HasPlugin(string gamePath) =>
        File.Exists(Path.Combine(PluginDirectory(gamePath), "StarTruckMP.Client.dll"));

    public static bool HasOverlay(string gamePath) =>
        File.Exists(Path.Combine(PluginDirectory(gamePath), "overlay", "StarTruckMP.Overlay.exe"));

    public async Task InstallAsync(string gamePath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(gamePath);

        if (HasBepInEx(gamePath))
        {
            progress.Report("BepInEx is already installed.");
        }
        else
        {
            progress.Report("Looking up the latest BepInEx (IL2CPP win-x64) release…");

            var url = await GitHubReleases
                .FindAssetUrlAsync("BepInEx", "BepInEx", IsBepInExAsset, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Could not find a BepInEx win-x64 zip in the BepInEx GitHub releases.");

            progress.Report($"Downloading {url[(url.LastIndexOf('/') + 1)..]}…");
            var archive = await Downloader.DownloadToTempAsync(url, progress, cancellationToken).ConfigureAwait(false);

            progress.Report("Extracting BepInEx into the game folder…");
            ZipFile.ExtractToDirectory(archive, gamePath, overwriteFiles: true);
            TryDelete(archive);

            if (!HasBepInEx(gamePath))
                throw new InvalidOperationException("BepInEx was extracted but the expected files are missing. The release layout may have changed.");
        }

        if (HasPlugin(gamePath))
        {
            progress.Report("StarTruckMP client is already installed.");
        }
        else
        {
            progress.Report("Looking up the latest StarTruckMP client release…");

            var url = await GitHubReleases
                .FindAssetUrlAsync(RepositoryOwner, RepositoryName, IsPluginAsset, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException(
                    $"Could not find the StarTruckMP client zip in {RepositoryOwner}/{RepositoryName} releases. " +
                    "Build it with build-client-winx64.cmd or publish a release first.");

            progress.Report($"Downloading {url[(url.LastIndexOf('/') + 1)..]}…");
            var archive = await Downloader.DownloadToTempAsync(url, progress, cancellationToken).ConfigureAwait(false);

            var pluginDirectory = PluginDirectory(gamePath);
            progress.Report($"Extracting the client into {pluginDirectory}…");
            Directory.CreateDirectory(pluginDirectory);
            ZipFile.ExtractToDirectory(archive, pluginDirectory, overwriteFiles: true);
            TryDelete(archive);

            if (!HasPlugin(gamePath))
                throw new InvalidOperationException("The client archive was extracted but StarTruckMP.Client.dll is missing.");
        }

        progress.Report("Multiplayer mod is ready.");
        _log.Log("Mod installation finished.");
    }

    private static bool IsBepInExAsset(string name) =>
        name.StartsWith("BepInEx_win_x64", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsPluginAsset(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("bepinex", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("server", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("docker", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignored - temp files are cleaned by the OS
        }
    }
}
