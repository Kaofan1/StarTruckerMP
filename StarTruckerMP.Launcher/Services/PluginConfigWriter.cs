using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StarTruckerMP.Launcher.Services;

/// <summary>
/// Writes the selected server into <c>BepInEx/config/StarTruckerMP.Client.cfg</c>,
/// preserving every other value the plugin (or the player) already stored there.
/// </summary>
internal static class PluginConfigWriter
{
    public const string ConfigRelativePath = @"BepInEx\config\StarTruckerMP.Client.cfg";

    public static string GetConfigPath(string gamePath) => Path.Combine(gamePath, ConfigRelativePath);

    public static void Apply(string gamePath, string address, string port, bool ignoreSslValidation, string microphoneDeviceName)
    {
        var configPath = GetConfigPath(gamePath);
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var content = File.Exists(configPath) ? File.ReadAllText(configPath) : DefaultTemplate;

        content = SetKey(content, "Connection", "ServerAddress", address);
        content = SetKey(content, "Connection", "ServerPort", port);
        content = SetKey(content, "Connection", "IgnoreSslValidation", ignoreSslValidation ? "true" : "false");

        // Always written: an empty value is how the client is told to auto-select,
        // so switching back to the system default really clears the old device.
        content = SetKey(content, "Audio", "MicrophoneDeviceName", microphoneDeviceName);

        File.WriteAllText(configPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string SetKey(string content, string section, string key, string value)
    {
        var newLine = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = new List<string>(content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));

        var sectionStart = -1;
        var sectionEnd = lines.Count;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal) || !trimmed.EndsWith("]", StringComparison.Ordinal))
                continue;

            if (sectionStart >= 0)
            {
                sectionEnd = index;
                break;
            }

            var name = trimmed[1..^1].Trim();
            if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                sectionStart = index;
        }

        if (sectionStart >= 0)
        {
            var lastMeaningfulLine = sectionStart;

            for (var index = sectionStart + 1; index < sectionEnd; index++)
            {
                var trimmed = lines[index].Trim();
                if (trimmed.Length == 0)
                    continue;

                lastMeaningfulLine = index;

                if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    var separatorIndex = trimmed.IndexOf('=');
                    var existingKey = separatorIndex < 0 ? trimmed : trimmed[..separatorIndex].Trim();
                    if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[index] = $"{key} = {value}";
                        return string.Join(newLine, lines);
                    }
                }
            }

            lines.Insert(lastMeaningfulLine + 1, $"{key} = {value}");
            return string.Join(newLine, lines);
        }

        if (lines.Count > 0 && lines[^1].Trim().Length != 0)
            lines.Add(string.Empty);

        lines.Add($"[{section}]");
        lines.Add(string.Empty);
        lines.Add($"{key} = {value}");
        lines.Add(string.Empty);

        return string.Join(newLine, lines);
    }

    private const string DefaultTemplate = """
        ## StarTruckerMP client configuration
        ## Managed by the Star Trucker Multiplayer launcher - manual edits are preserved.

        [Connection]

        ## StarTruckerMP server address
        # Setting type: String
        # Default value: 127.0.0.1
        ServerAddress = 127.0.0.1

        ## StarTruckerMP server port
        # Setting type: String
        # Default value: 7777
        ServerPort = 7777

        ## Ignore SSL certificate validation errors (self-signed servers use this)
        # Setting type: Boolean
        # Default value: false
        IgnoreSslValidation = false

        [Audio]

        ## Exact microphone device name to use. Leave empty to auto-select.
        # Setting type: String
        # Default value:
        MicrophoneDeviceName =

        ## Final output gain applied after the NWaves radio voice effect.
        # Setting type: Single
        # Default value: 1
        RadioEffectOutputGain = 1
        """;
}
