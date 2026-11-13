using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace StarTruckerMP.Launcher.Models;

public sealed class LauncherSettings
{
    public string? GamePath { get; set; }

    /// <summary>UI language code: "en" or "pl". Missing means English.</summary>
    public string? Language { get; set; }

    /// <summary>Last window size in logical pixels; null means the default size.</summary>
    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    /// <summary>Last window position in physical pixels; null means the default centred window.</summary>
    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public bool LaunchThroughSteam { get; set; } = true;

    public bool IgnoreSslValidation { get; set; } = true;

    public bool CloseLauncherAfterLaunch { get; set; } = true;

    public string MicrophoneDeviceName { get; set; } = string.Empty;

    public string? SelectedServerId { get; set; }

    /// <summary>Base URL of the public server directory, for example https://hub.example:7777.</summary>
    public string? DirectoryUrl { get; set; }

    public List<ServerEntry> Servers { get; set; } = new();

    [JsonIgnore]
    public ServerEntry? SelectedServer =>
        Servers.FirstOrDefault(server => server.Id == SelectedServerId) ?? Servers.FirstOrDefault();

    public static LauncherSettings CreateDefault()
    {
        var settings = new LauncherSettings();
        var server = new ServerEntry();
        settings.Servers.Add(server);
        settings.SelectedServerId = server.Id;
        return settings;
    }
}
