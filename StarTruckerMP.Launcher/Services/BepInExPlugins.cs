using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using StarTruckerMP.Launcher.Localization;

namespace StarTruckerMP.Launcher.Services;

/// <summary>One entry of <c>BepInEx\plugins</c>: a plugin folder or a loose plugin DLL.</summary>
internal sealed record PluginEntry(string Name, string Path, bool IsDirectory, bool IsOwnClient, bool IsEnabled);

/// <summary>
/// Manages the plugin half of BepInEx: it lists what the player has in <c>BepInEx\plugins</c> and
/// copies new mods there. The loader itself belongs to <see cref="Mod.BepInExRuntime"/>.
/// </summary>
internal static class BepInExPlugins
{
    public const string OwnClientFolder = "StarTruckerMP";
    public const string LegacyClientFolder = "StarTruckMP";

    /// <summary>Folder names and extensions used to switch a plugin off without deleting it.</summary>
    public const string DisabledExtension = ".disabled";

    public static string PluginsDirectory(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "plugins");

    /// <summary>Where disabled plugin folders are parked; BepInEx does not scan it.</summary>
    public static string DisabledDirectory(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "plugins-disabled");

    /// <summary>
    /// Folders and loose DLLs of the plugins folder, the StarTruckerMP client first. Disabled
    /// entries are listed too, so switching one back on does not need Explorer.
    /// </summary>
    public static IReadOnlyList<PluginEntry> List(string gamePath)
    {
        var directory = PluginsDirectory(gamePath);
        var disabledDirectory = DisabledDirectory(gamePath);
        var entries = new List<PluginEntry>();

        if (Directory.Exists(directory))
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(child);
                entries.Add(new PluginEntry(name, child, IsDirectory: true, IsOwnClient(name), IsEnabled: true));
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
            {
                entries.Add(new PluginEntry(Path.GetFileName(file), file, IsDirectory: false, IsOwnClient: false, IsEnabled: true));
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*" + DisabledExtension))
            {
                var name = Path.GetFileName(file);
                if (!name.EndsWith(".dll" + DisabledExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                // The stored name is "MyMod.dll.disabled"; show it as the plugin it used to be.
                entries.Add(new PluginEntry(name[..^DisabledExtension.Length], file, IsDirectory: false, IsOwnClient: false, IsEnabled: false));
            }
        }

        if (Directory.Exists(disabledDirectory))
        {
            foreach (var child in Directory.EnumerateDirectories(disabledDirectory))
            {
                var name = Path.GetFileName(child);
                entries.Add(new PluginEntry(name, child, IsDirectory: true, IsOwnClient: IsOwnClient(name), IsEnabled: false));
            }
        }

        return entries
            .OrderBy(entry => entry.IsOwnClient ? 0 : 1)
            .ThenByDescending(entry => entry.IsEnabled)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Switches a plugin off without deleting it: a loose DLL gets the .disabled extension, a
    /// plugin folder is moved to BepInEx\plugins-disabled. Returns the new path.
    /// </summary>
    public static string Disable(string gamePath, string pluginPath)
    {
        var target = ResolveInside(pluginPath, gamePath);

        if (IsOwnClient(Path.GetFileName(target)))
            throw new InvalidOperationException(Strings.PluginToggleOwnClient);

        if (Directory.Exists(target))
        {
            var disabledDirectory = DisabledDirectory(gamePath);
            Directory.CreateDirectory(disabledDirectory);

            var destination = Path.Combine(disabledDirectory, Path.GetFileName(target));
            if (Directory.Exists(destination))
                throw new InvalidOperationException(Strings.PluginDisabledNameTaken(Path.GetFileName(target)));

            Directory.Move(target, destination);
            return destination;
        }

        if (!File.Exists(target))
            throw new InvalidOperationException(Strings.PluginAlreadyGone(Path.GetFileName(target)));

        var disabled = target + DisabledExtension;
        File.Move(target, disabled, overwrite: true);
        return disabled;
    }

    /// <summary>Switches a plugin back on; the mirror image of <see cref="Disable"/>.</summary>
    public static string Enable(string gamePath, string pluginPath)
    {
        var target = Path.GetFullPath(pluginPath);
        var directory = PluginsDirectory(gamePath);
        Directory.CreateDirectory(directory);

        if (Directory.Exists(target))
        {
            var destination = Path.Combine(directory, Path.GetFileName(target));
            if (Directory.Exists(destination))
                throw new InvalidOperationException(Strings.PluginEnabledNameTaken(Path.GetFileName(target)));

            Directory.Move(target, destination);
            return destination;
        }

        if (!File.Exists(target))
            throw new InvalidOperationException(Strings.PluginAlreadyGone(Path.GetFileName(target)));

        var name = Path.GetFileName(target);
        var enabledName = name.EndsWith(DisabledExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^DisabledExtension.Length]
            : name;

        var enabled = Path.Combine(directory, enabledName);
        File.Move(target, enabled, overwrite: true);
        return enabled;
    }

    /// <summary>Remove already checks this; the toggles share the same rule.</summary>
    private static string ResolveInside(string pluginPath, string gamePath)
    {
        var plugins = Path.GetFullPath(PluginsDirectory(gamePath)) + Path.DirectorySeparatorChar;
        var disabled = Path.GetFullPath(DisabledDirectory(gamePath)) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(pluginPath);

        if (!target.StartsWith(plugins, StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith(disabled, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.PluginOutsidePlugins(pluginPath));

        return target;
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
        var target = ResolveInside(pluginPath, gamePath);

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
