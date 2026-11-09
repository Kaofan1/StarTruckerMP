using System;
using System.IO;
using Avalonia;
using StarTruckMP.Launcher.Services;

namespace StarTruckMP.Launcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            TryWriteCrashLog(exception);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<LauncherApp>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static void TryWriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDirectory);
            File.AppendAllText(
                Path.Combine(SettingsStore.DataDirectory, "launcher-crash.log"),
                $"[{DateTime.Now:u}] {exception}{Environment.NewLine}");
        }
        catch
        {
            // ignored - there is nowhere else to report the failure
        }
    }
}
