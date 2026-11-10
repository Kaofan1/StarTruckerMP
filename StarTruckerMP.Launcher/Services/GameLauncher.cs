using System;
using System.Diagnostics;
using System.IO;

namespace StarTruckerMP.Launcher.Services;

internal static class GameLauncher
{
    /// <summary>
    /// Star Trucker uses Steamworks for authentication, so the game must be started by
    /// Steam (steam://rungameid) rather than by its executable whenever possible.
    /// </summary>
    public static void Launch(GameInstall install, bool throughSteam)
    {
        if (throughSteam)
        {
            Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamLibraryLocator.StarTruckerAppId}")
            {
                UseShellExecute = true
            });
            return;
        }

        var executable = Path.Combine(install.Path, GameInstall.ExecutableName);
        Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = install.Path,
            UseShellExecute = true
        });
    }

    public static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
