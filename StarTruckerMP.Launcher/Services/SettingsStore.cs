using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarTruckerMP.Launcher.Models;

namespace StarTruckerMP.Launcher.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StarTruckerMP");

    public static string SettingsPath => Path.Combine(DataDirectory, "launcher.json");

    /// <summary>Folder used before the launcher was renamed from StarTruckMP.</summary>
    public static string LegacyDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StarTruckMP");

    /// <summary>
    /// Copies the previous launcher.json once, so an upgrade keeps servers, language, launch options
    /// and window geometry instead of silently starting from defaults. The old folder is left alone.
    /// </summary>
    public static bool MigrateLegacySettings()
    {
        try
        {
            var legacySettings = Path.Combine(LegacyDataDirectory, "launcher.json");
            if (File.Exists(SettingsPath) || !File.Exists(legacySettings))
                return false;

            Directory.CreateDirectory(DataDirectory);
            File.Copy(legacySettings, SettingsPath);
            return true;
        }
        catch
        {
            // Settings are convenience state only; a failed copy must never block startup.
            return false;
        }
    }

    public LauncherSettings Load()
    {
        MigrateLegacySettings();

        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (settings is not null)
                {
                    if (settings.Servers is null || settings.Servers.Count == 0)
                        settings.Servers = new List<ServerEntry> { new() };

                    if (settings.Servers.All(server => server.Id != settings.SelectedServerId))
                        settings.SelectedServerId = settings.Servers[0].Id;

                    return settings;
                }
            }
        }
        catch
        {
            // A corrupt settings file must never block the launcher; fall back to defaults.
        }

        var defaults = LauncherSettings.CreateDefault();
        Save(defaults);
        return defaults;
    }

    public void Save(LauncherSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // ignored - settings are convenience state only
        }
    }
}
