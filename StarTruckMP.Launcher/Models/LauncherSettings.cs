using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace StarTruckMP.Launcher.Models;

public sealed class LauncherSettings
{
    public string? GamePath { get; set; }

    public bool LaunchThroughSteam { get; set; } = true;

    public bool IgnoreSslValidation { get; set; } = true;

    public bool CloseLauncherAfterLaunch { get; set; } = true;

    public string MicrophoneDeviceName { get; set; } = string.Empty;

    public string? SelectedServerId { get; set; }

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
