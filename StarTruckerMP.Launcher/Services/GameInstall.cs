using System;
using System.IO;

namespace StarTruckerMP.Launcher.Services;

/// <summary>
/// A Star Trucker installation directory that passed validation.
/// </summary>
public sealed class GameInstall
{
    public const string ExecutableName = "Star Trucker.exe";

    public string Path { get; init; } = string.Empty;

    public bool IsIl2Cpp { get; init; }

    public bool IsXboxInstall { get; init; }

    public string? Warning
    {
        get
        {
            if (IsXboxInstall)
                return "This looks like an Xbox / Game Pass install. The mod targets the Steam version of Star Trucker.";

            if (!IsIl2Cpp)
                return "GameAssembly.dll was not found - Star Trucker is expected to be an IL2CPP build, so the mod may not load.";

            return null;
        }
    }

    public static GameInstall? FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        if (!File.Exists(System.IO.Path.Combine(fullPath, ExecutableName)))
            return null;

        return new GameInstall
        {
            Path = fullPath,
            IsIl2Cpp = File.Exists(System.IO.Path.Combine(fullPath, "GameAssembly.dll")),
            IsXboxInstall =
                fullPath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains("XboxGames", StringComparison.OrdinalIgnoreCase)
        };
    }
}
