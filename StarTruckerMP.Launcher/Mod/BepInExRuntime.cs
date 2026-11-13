using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarTruckerMP.Launcher.Localization;
using StarTruckerMP.Launcher.Services;

namespace StarTruckerMP.Launcher.Mod;

/// <summary>
/// The BepInEx half of the mod, deliberately kept apart from the launcher and from the client
/// installer. The launcher is a plain Star Trucker launcher: it installs nothing game-side on its
/// own, and BepInEx is only added when the user explicitly installs the multiplayer mod.
/// The loader files come from the optional <c>payload\bepinex</c> folder next to the launcher
/// (offline drop-in) or, when that folder is empty, from the BepInEx 6 bleeding-edge builds.
/// </summary>
internal static class BepInExRuntime
{
    private const string Il2CppCoreMarker = "BepInEx.Unity.IL2CPP.dll";
    private const string MonoCoreMarker = "BepInEx.dll";

    /// <summary>Loader files the install places in the game root, removed again on uninstall.</summary>
    private static readonly string[] LoaderRootFiles =
    {
        "winhttp.dll",
        "doorstop_config.ini",
        ".doorstop_version"
    };

    public static string CoreDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "core");

    /// <summary>Optional offline payload folder shipped next to the launcher executable.</summary>
    public static string DefaultPayloadRoot =>
        Path.Combine(AppContext.BaseDirectory, "payload", "bepinex");

    /// <summary>
    /// True when a BepInEx 6 IL2CPP loader is in place. The IL2CPP core is the marker:
    /// a BepInEx 5 (Mono) install has <c>BepInEx.dll</c> instead and never loads this plugin.
    /// </summary>
    public static bool HasBepInEx(string gamePath) =>
        File.Exists(Path.Combine(gamePath, "winhttp.dll")) &&
        File.Exists(Path.Combine(gamePath, "doorstop_config.ini")) &&
        File.Exists(Path.Combine(CoreDirectory(gamePath), Il2CppCoreMarker));

    /// <summary>True when the folder holds a BepInEx 5 (Mono) install, which cannot load the mod.</summary>
    public static bool HasMonoBepInEx(string gamePath) =>
        !HasBepInEx(gamePath) &&
        File.Exists(Path.Combine(CoreDirectory(gamePath), MonoCoreMarker));

    /// <summary>
    /// Removes the loader files and <c>BepInEx\core</c>, but keeps <c>BepInEx\plugins</c> and
    /// <c>BepInEx\config</c>: a repair or a later reinstall must not throw away mod settings.
    /// Throws when the game holds the files open.
    /// </summary>
    public static bool Uninstall(string gamePath)
    {
        var removed = false;

        foreach (var name in LoaderRootFiles)
        {
            var path = Path.Combine(gamePath, name);
            if (!File.Exists(path))
                continue;

            File.Delete(path);
            removed = true;
        }

        var core = CoreDirectory(gamePath);
        if (Directory.Exists(core))
        {
            Directory.Delete(core, recursive: true);
            removed = true;
        }

        return removed;
    }

    /// <summary>
    /// Ensures the IL2CPP loader is present in the game folder. Does nothing when it already is -
    /// repairing the loader is a separate concern from updating the client.
    /// </summary>
    public static async Task InstallAsync(
        string gamePath,
        string payloadRoot,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(gamePath);

        if (HasBepInEx(gamePath))
        {
            progress.Report(Strings.BepInExAlreadyInstalled);
            return;
        }

        if (HasMonoBepInEx(gamePath))
        {
            // Mixing BepInEx 5 with the IL2CPP loader leaves two loaders fighting over the
            // same folder, so the old one is moved aside instead of being overwritten.
            var backup = Path.Combine(gamePath, $"BepInEx.mono-backup-{DateTime.Now:yyyyMMddHHmmss}");
            progress.Report(Strings.BepInExMonoBackup(Path.GetFileName(backup)));
            Directory.Move(Path.Combine(gamePath, "BepInEx"), backup);
        }

        var payload = FindPayload(payloadRoot);

        if (payload is not null)
        {
            progress.Report(Strings.BepInExUsingPayload(payloadRoot));

            try
            {
                if (Directory.Exists(payload))
                    CopyDirectory(payload, gamePath);
                else
                    ZipFile.ExtractToDirectory(payload, gamePath, overwriteFiles: true);

                FlattenSingleTopLevelFolder(gamePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    Strings.BepInExPayloadWriteFailed(exception.Message),
                    exception);
            }
        }
        else
        {
            progress.Report(Strings.BepInExLookingUp);

            var (url, build) = await BepInExSource
                .FindLatestAsync(cancellationToken)
                .ConfigureAwait(false);

            progress.Report(Strings.BepInExDownloading(build));
            var archive = await Downloader.DownloadToTempAsync(url, progress, cancellationToken).ConfigureAwait(false);

            try
            {
                progress.Report(Strings.BepInExExtracting);
                ZipFile.ExtractToDirectory(archive, gamePath, overwriteFiles: true);
                FlattenSingleTopLevelFolder(gamePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    Strings.BepInExWriteFailed(exception.Message),
                    exception);
            }
            finally
            {
                TryDelete(archive);
            }
        }

        if (!HasBepInEx(gamePath))
            throw new InvalidOperationException(Strings.BepInExLayoutChanged);

        progress.Report(Strings.BepInExReady);
    }

    /// <summary>
    /// The payload folder may hold either extracted BepInEx files or a single BepInEx zip.
    /// Returns the path to use, or null when the folder is missing or empty.
    /// </summary>
    private static string? FindPayload(string payloadRoot)
    {
        if (!Directory.Exists(payloadRoot))
            return null;

        if (File.Exists(Path.Combine(payloadRoot, "winhttp.dll")))
            return payloadRoot;

        if (Directory.Exists(Path.Combine(payloadRoot, "BepInEx")))
            return payloadRoot;

        return Directory
            .EnumerateFiles(payloadRoot, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    /// <summary>
    /// Some archives wrap their contents in a single folder. BepInEx has to sit directly in
    /// the game root, so such a folder is dissolved into the game root.
    /// </summary>
    private static void FlattenSingleTopLevelFolder(string gamePath)
    {
        if (Directory.Exists(Path.Combine(gamePath, "BepInEx")))
            return;

        foreach (var candidate in Directory.EnumerateDirectories(gamePath))
        {
            if (!Directory.Exists(Path.Combine(candidate, "BepInEx")))
                continue;

            foreach (var entry in Directory.EnumerateFileSystemEntries(candidate))
                Directory.Move(entry, Path.Combine(gamePath, Path.GetFileName(entry)));

            Directory.Delete(candidate, recursive: true);
            return;
        }
    }

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
