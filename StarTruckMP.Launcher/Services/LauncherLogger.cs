using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StarTruckMP.Launcher.Services;

internal sealed class LauncherLogger
{
    private const int MaxInMemoryLines = 2000;

    private readonly object _syncRoot = new();
    private readonly List<string> _lines = new();

    public LauncherLogger()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDirectory);
        }
        catch
        {
            // ignored
        }
    }

    public static string LogPath => Path.Combine(SettingsStore.DataDirectory, "launcher.log");

    public event Action<string>? LineWritten;

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (_syncRoot)
        {
            _lines.Add(line);
            if (_lines.Count > MaxInMemoryLines)
                _lines.RemoveRange(0, _lines.Count - MaxInMemoryLines);

            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // ignored - logging must not break the launcher
            }
        }

        LineWritten?.Invoke(line);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_syncRoot)
        {
            return _lines.ToArray();
        }
    }
}
