using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using StarTruckerMP.Launcher.Localization;
using StarTruckerMP.Launcher.Mod;

namespace StarTruckerMP.Launcher.Services;

/// <summary>
/// Installs the StarTruckerMP client plugin (and its overlay) into the game folder.
/// The loader half lives in <see cref="BepInExRuntime"/>: this class never downloads or
/// places BepInEx itself, so the launcher works with or without the multiplayer mod.
/// </summary>
internal sealed class ModInstaller
{
    public const string RepositoryOwner = "Kaofan1";
    public const string RepositoryName = "StarTruckerMP";

    /// <summary>Name of the game process without the .exe suffix.</summary>
    private const string GameProcessName = "Star Trucker";

    private readonly LauncherLogger _log;

    public ModInstaller(LauncherLogger log)
    {
        _log = log;
    }

    public static string PluginDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "plugins", "StarTruckerMP");

    /// <summary>Plugin folder written before the rename; left in place it loads a second copy of the mod.</summary>
    public static string LegacyPluginDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "plugins", "StarTruckMP");

    /// <summary>Best effort cleanup of the pre-rename plugin folder after a successful install.</summary>
    public static bool RemoveLegacyPluginDirectory(string gamePath)
    {
        try
        {
            var legacyDirectory = LegacyPluginDirectory(gamePath);
            if (!Directory.Exists(legacyDirectory))
                return false;

            Directory.Delete(legacyDirectory, recursive: true);
            return true;
        }
        catch
        {
            // Locked files only leave the old copy behind; the install itself already succeeded.
            return false;
        }
    }

    public static string ClientAssemblyPath(string gamePath) =>
        Path.Combine(PluginDirectory(gamePath), "StarTruckerMP.Client.dll");

    public static string OverlayExecutablePath(string gamePath) =>
        Path.Combine(PluginDirectory(gamePath), "overlay", "StarTruckerMP.Overlay.exe");

    /// <summary>
    /// Loader markers are owned by the isolated BepInEx component; these forwarders keep the
    /// rest of the launcher free of loader details.
    /// </summary>
    public static bool HasBepInEx(string gamePath) => BepInExRuntime.HasBepInEx(gamePath);

    /// <inheritdoc cref="BepInExRuntime.HasMonoBepInEx"/>
    public static bool HasMonoBepInEx(string gamePath) => BepInExRuntime.HasMonoBepInEx(gamePath);

    public static bool HasPlugin(string gamePath) => File.Exists(ClientAssemblyPath(gamePath));

    public static bool HasOverlay(string gamePath) => File.Exists(OverlayExecutablePath(gamePath));

    /// <summary>Windows keeps plugin DLLs locked while the game runs, so it has to be closed first.</summary>
    public static bool IsGameRunning()
    {
        try
        {
            return Process.GetProcessesByName(GameProcessName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Installs/repairs only the client plugin and its overlay, never the loader.</summary>
    public async Task InstallAsync(string gamePath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(gamePath);

        if (IsGameRunning())
            throw new InvalidOperationException(Strings.GameRunningBlock);

        await InstallClientAsync(gamePath, progress, cancellationToken).ConfigureAwait(false);

        progress.Report(Strings.ClientReady);
        progress.Report(Strings.FirstLaunchNote);
        _log.Log(Strings.ClientInstallFinished);
    }

    private async Task InstallClientAsync(
        string gamePath,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        // The client is always refreshed: that is what makes the same button repair a
        // half-copied install and update an outdated plugin.
        progress.Report(Strings.ClientLookingUp);

        var url = await GitHubReleases
            .FindAssetUrlAsync(RepositoryOwner, RepositoryName, IsPluginAsset, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(Strings.ClientPackageMissing);

        progress.Report(Strings.ClientDownloading(url[(url.LastIndexOf('/') + 1)..]));
        var archive = await Downloader.DownloadToTempAsync(url, progress, cancellationToken).ConfigureAwait(false);

        var pluginDirectory = PluginDirectory(gamePath);

        try
        {
            progress.Report(Strings.ClientInstalling(pluginDirectory));
            if (Directory.Exists(pluginDirectory))
                Directory.Delete(pluginDirectory, recursive: true);

            Directory.CreateDirectory(pluginDirectory);
            ZipFile.ExtractToDirectory(archive, pluginDirectory, overwriteFiles: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(Strings.ClientWriteFailed(exception.Message), exception);
        }
        finally
        {
            TryDelete(archive);
        }

        if (!HasPlugin(gamePath))
            throw new InvalidOperationException(Strings.ClientAssemblyMissing);

        if (!HasOverlay(gamePath))
        {
            progress.Report(Strings.ClientWithoutOverlay);
            _log.Log(Strings.OverlayMissingFromArchive);
        }

        if (RemoveLegacyPluginDirectory(gamePath))
            _log.Log(Strings.LegacyPluginRemoved);

        _log.Log(Strings.ClientInstalledFrom(url));
    }

    private static bool IsPluginAsset(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("bepinex", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("server", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("docker", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("launcher", StringComparison.OrdinalIgnoreCase);

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
