using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using StarTruckMP.Launcher.Mod;

namespace StarTruckMP.Launcher.Services;

/// <summary>
/// Installs the StarTruckMP client plugin (and its overlay) into the game folder.
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

    public static string PluginDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "plugins", "StarTruckMP");

    public static string ClientAssemblyPath(string gamePath) =>
        Path.Combine(PluginDirectory(gamePath), "StarTruckMP.Client.dll");

    public static string OverlayExecutablePath(string gamePath) =>
        Path.Combine(PluginDirectory(gamePath), "overlay", "StarTruckMP.Overlay.exe");

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
            throw new InvalidOperationException(
                "Star Trucker is running. Close the game first — Windows keeps the mod files locked while it is open.");

        await InstallClientAsync(gamePath, progress, cancellationToken).ConfigureAwait(false);

        progress.Report("Multiplayer client is ready.");
        progress.Report(
            "The first launch after installing generates the BepInEx IL2CPP interop assemblies and can take a few minutes. " +
            "If the overlay does not appear the first time, restart the game once.");
        _log.Log("Client installation finished.");
    }

    private async Task InstallClientAsync(
        string gamePath,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        // The client is always refreshed: that is what makes the same button repair a
        // half-copied install and update an outdated plugin.
        progress.Report("Looking up the latest StarTruckMP client release…");

        var url = await GitHubReleases
            .FindAssetUrlAsync(RepositoryOwner, RepositoryName, IsPluginAsset, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(
                $"None of the {RepositoryOwner}/{RepositoryName} releases contains the mod package yet " +
                "(StarTruckMP-bepinex-plugin-win-x64.zip). The launcher keeps working without it, but the multiplayer " +
                "client has to be published in a release before the launcher can add it — please report this in the Issues tab.");

        progress.Report($"Downloading {url[(url.LastIndexOf('/') + 1)..]}…");
        var archive = await Downloader.DownloadToTempAsync(url, progress, cancellationToken).ConfigureAwait(false);

        var pluginDirectory = PluginDirectory(gamePath);

        try
        {
            progress.Report($"Installing the client into {pluginDirectory}…");
            if (Directory.Exists(pluginDirectory))
                Directory.Delete(pluginDirectory, recursive: true);

            Directory.CreateDirectory(pluginDirectory);
            ZipFile.ExtractToDirectory(archive, pluginDirectory, overwriteFiles: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The client files could not be written into the game folder. Make sure the game is closed; if it is " +
                "installed under Program Files, run the launcher as administrator. Details: " + exception.Message,
                exception);
        }
        finally
        {
            TryDelete(archive);
        }

        if (!HasPlugin(gamePath))
            throw new InvalidOperationException("The client archive was extracted but StarTruckMP.Client.dll is missing.");

        if (!HasOverlay(gamePath))
        {
            progress.Report("Warning: the release does not contain the overlay. The radio UI will not be available.");
            _log.Log("Warning: overlay/StarTruckMP.Overlay.exe is missing from the client archive.");
        }

        _log.Log("Client plugin installed from " + url);
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
