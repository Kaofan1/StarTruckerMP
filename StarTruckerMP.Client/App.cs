using System.Text.Json;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace StarTruckerMP.Client;

public static class App
{
    public static ConfigEntry<string> ServerAddress;
    public static ConfigEntry<string> ServerPort;
    public static ConfigEntry<string> MicrophoneDeviceName;
    public static ConfigEntry<bool> PreferSystemDefaultMicrophone;
    public static ConfigEntry<float> RadioEffectOutputGain;
    public static ConfigEntry<bool> IgnoreSslValidation;
    public static ConfigEntry<string> DirectoryUrl;
    public static ConfigEntry<bool> MenuButtonEnabled;
    public static ConfigFile ConfigFile;

    public static void Configure(ConfigFile config)
    {
        ConfigFile = config;
        ServerAddress = config.Bind("Connection", "ServerAddress", "127.0.0.1", "StarTruckerMP server address");
        ServerPort = config.Bind("Connection", "ServerPort", "7777", "StarTruckerMP server port");
        DirectoryUrl = config.Bind("Connection", "DirectoryUrl", "https://127.0.0.1:7777", "Server directory read by the Multiplayer panel in the main menu");
        MenuButtonEnabled = config.Bind("Interface", "MenuButton", true, "Add a Multiplayer entry to the game's main menu. F9 always opens the same panel.");
        MicrophoneDeviceName = config.Bind("Audio", "MicrophoneDeviceName", string.Empty, "Exact microphone device name to use. Leave empty to auto-select.");
        PreferSystemDefaultMicrophone = config.Bind("Audio", "PreferSystemDefaultMicrophone", true, "When auto-selecting, try the Windows default microphone before explicit devices.");
        RadioEffectOutputGain = config.Bind("Audio", "RadioEffectOutputGain", 1.0f, "Final output gain applied after the NWaves radio voice effect.");
        IgnoreSslValidation = config.Bind("Connection", "IgnoreSslValidation", false, "Whether to ignore SSL certificate validation errors. Not recommended for production use.");
    }

    public static ManualLogSource Log;
    
    public static JsonSerializerOptions JsonReaderOptions = new() { PropertyNameCaseInsensitive = true };
    public static JsonSerializerOptions JsonWriterOptions = new() { WriteIndented = false };
}