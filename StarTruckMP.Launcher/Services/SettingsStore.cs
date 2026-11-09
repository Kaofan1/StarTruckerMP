using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarTruckMP.Launcher.Models;

namespace StarTruckMP.Launcher.Services;

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
        "StarTruckMP");

    public static string SettingsPath => Path.Combine(DataDirectory, "launcher.json");

    public LauncherSettings Load()
    {
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
