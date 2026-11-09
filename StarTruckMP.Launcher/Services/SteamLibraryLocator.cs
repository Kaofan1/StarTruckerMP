using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StarTruckMP.Launcher.Services;

/// <summary>
/// Resolves the Steam installation and every library folder it knows about,
/// then looks for the Star Trucker app (2380050) inside them.
/// </summary>
internal static class SteamLibraryLocator
{
    public const uint StarTruckerAppId = 2380050;

    private const string StarTruckerFolderName = "Star Trucker";

    private static readonly Regex LibraryPathRegex =
        new("\"path\"\\s*\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InstallDirRegex =
        new("\"installdir\"\\s*\"(?<dir>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? FindSteamRoot()
    {
        string?[] candidates =
        [
            ReadRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            ReadRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            Combine(Environment.SpecialFolder.ProgramFilesX86, "Steam"),
            Combine(Environment.SpecialFolder.ProgramFiles, "Steam")
        ];

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static IReadOnlyList<string> FindLibraryFolders(string steamRoot)
    {
        var libraries = new List<string>();
        AddIfExists(libraries, steamRoot);

        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
            return libraries;

        string content;
        try
        {
            content = File.ReadAllText(libraryFile);
        }
        catch
        {
            return libraries;
        }

        foreach (Match match in LibraryPathRegex.Matches(content))
        {
            var path = match.Groups["path"].Value.Replace(@"\\", @"\");
            AddIfExists(libraries, path);
        }

        return libraries;
    }

    public static string? FindStarTruckerInstall()
    {
        var steamRoot = FindSteamRoot();
        if (steamRoot is not null)
        {
            foreach (var library in FindLibraryFolders(steamRoot))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{StarTruckerAppId}.acf");
                if (File.Exists(manifest))
                {
                    try
                    {
                        var match = InstallDirRegex.Match(File.ReadAllText(manifest));
                        if (match.Success)
                        {
                            var installed = Path.Combine(library, "steamapps", "common", match.Groups["dir"].Value);
                            if (GameInstall.FromPath(installed) is not null)
                                return installed;
                        }
                    }
                    catch
                    {
                        // fall through to the well-known folder name
                    }
                }

                var defaultFolder = Path.Combine(library, "steamapps", "common", StarTruckerFolderName);
                if (GameInstall.FromPath(defaultFolder) is not null)
                    return defaultFolder;
            }
        }

        foreach (var guess in EnumerateCommonLocations())
        {
            if (GameInstall.FromPath(guess) is not null)
                return guess;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCommonLocations()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                continue;

            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "XboxGames", StarTruckerFolderName, "Content");
            yield return Path.Combine(root, "Games", StarTruckerFolderName);
            yield return Path.Combine(root, "SteamLibrary", "steamapps", "common", StarTruckerFolderName);
        }
    }

    private static void AddIfExists(List<string> libraries, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        foreach (var existing in libraries)
        {
            if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                return;
        }

        libraries.Add(path);
    }

    private static string? ReadRegistry(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            return Registry.GetValue($@"{root.Name}\{subKey}", valueName, null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? Combine(Environment.SpecialFolder folder, string child)
    {
        var root = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, child);
    }
}
