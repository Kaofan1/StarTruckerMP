using System;
using System.Linq;
using BepInEx.Logging;

namespace StarTruckerMP.Client.Patches;

/// <summary>
/// TextMeshPro warns about every missing glyph and the affected texts refresh constantly, which
/// floods the console. The two known font asset messages are dropped at the listener level, since
/// the interop build does not expose UnityEngine.Debug in a way Harmony can patch.
/// </summary>
internal static class LogNoiseFilter
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;

        _installed = true;

        try
        {
            var existing = Logger.Listeners.ToArray();
            Logger.Listeners.Clear();
            Logger.Listeners.Add(new FilteringListener(existing));
        }
        catch (Exception exception)
        {
            App.Log.LogWarning("[Log] Could not install the font warning filter: " + exception.Message);
        }
    }

    private static bool IsFontWarning(string text) =>
        text.Contains("is not available in font asset", StringComparison.Ordinal)
        || (text.Contains("was not found in the", StringComparison.Ordinal)
            && text.Contains("SDF]", StringComparison.Ordinal));

    private sealed class FilteringListener : ILogListener
    {
        private readonly ILogListener[] _inner;

        public FilteringListener(ILogListener[] inner) => _inner = inner;

        public LogLevel LogLevelFilter => LogLevel.All;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            var text = eventArgs.Data?.ToString();
            if (text != null && IsFontWarning(text))
                return;

            foreach (var listener in _inner)
                listener.LogEvent(sender, eventArgs);
        }

        public void Dispose()
        {
            foreach (var listener in _inner)
                listener.Dispose();
        }
    }
}
