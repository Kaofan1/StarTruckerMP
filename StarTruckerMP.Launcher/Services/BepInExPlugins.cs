using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using StarTruckerMP.Launcher.Localization;

namespace StarTruckerMP.Launcher.Services;

/// <summary>One entry of <c>BepInEx\plugins</c>: a plugin folder or a loose plugin DLL.</summary>
internal sealed record PluginEntry(string Name, string Path, bool IsDirectory, bool IsOwnClient);

/// <summary>
/// Manages the plugin half of BepInEx: it lists what the player has in <c>BepInEx\plugins</c> and
/// copies new mods there. The loader itself belongs to <see cref="Mod.BepInExRuntime"/>.
/// </summary>
internal static class BepInExPlugins
{
    public const string OwnClientFolder = "StarTruckerMP";
    public const string LegacyClientFolder = "StarTruckMP";

    public static string PluginsDirectory(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "plugins");

    /// <summary>Folders and loose DLLs of the plugins folder, the StarTruckerMP client first.</summary>
    public static IReadOnlyList<PluginEntry> List(string gamePath)
    {
        var directory = PluginsDirectory(gamePath);
        if (!Directory.Exists(directory))
            return Array.Empty<PluginEntry>();

        var entries = new List<PluginEntry>();

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            entries.Add(new PluginEntry(name, child, IsDirectory: true, IsOwnClient(name)));
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
            entries.Add(new PluginEntry(Path.GetFileName(file), file, IsDirectory: false, IsOwnClient: false));

        return entries
            .OrderBy(entry => entry.IsOwnClient ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsOwnClient(string folderName) =>
        folderName.Equals(OwnClientFolder, StringComparison.OrdinalIgnoreCase) ||
        folderName.Equals(LegacyClientFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deletes one plugin folder or DLL. The path is checked against the plugins folder first, and
    /// our own client is refused because its own card owns that install.
    /// </summary>
    public static bool Remove(string gamePath, string pluginPath)
    {
        var directory = Path.GetFullPath(PluginsDirectory(gamePath)) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(pluginPath);

        if (!target.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.PluginOutsidePlugins(pluginPath));

        if (IsOwnClient(Path.GetFileName(target)))
            throw new InvalidOperationException(Strings.PluginRemoveOwnClient);

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
            return true;
        }

        if (File.Exists(target))
        {
            File.Delete(target);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Copies a single DLL or extracts a plugin archive into <c>BepInEx\plugins</c>. Existing files
    /// are overwritten, so dropping the same mod twice updates it instead of failing.
    /// </summary>
    public static string Install(string gamePath, string sourcePath)
    {
        var directory = PluginsDirectory(gamePath);
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(sourcePath);

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(sourcePath);

            // The archive comes from the player, so nothing may escape the plugins folder.
            var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var destination = Path.GetFullPath(Path.Combine(directory, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Strings.PluginArchiveUnsafe(entry.FullName));
            }

            archive.ExtractToDirectory(directory, overwriteFiles: true);
            return directory;
        }

        if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.PluginUnsupported(Path.GetFileName(sourcePath)));

        var target = Path.Combine(directory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }
}
