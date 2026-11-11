using Avalonia.Input;
using StarTruckerMP.Overlay.Core.Abstractions;
using StarTruckerMP.Overlay.Core.Services;
using StarTruckerMP.Overlay.Host.UI;

namespace StarTruckerMP.Overlay.Host.Services;

internal sealed class HotkeyService : IDisposable
{
    private readonly OverlayWindow _window;
    private readonly OverlayModeService _modeService;
    private readonly IOverlayLogger _logger;
    private bool _disposed;

    public HotkeyService(OverlayWindow window, OverlayModeService modeService, IOverlayLogger logger)
    {
        _window = window;
        _modeService = modeService;
        _logger = logger;
        _window.KeyDown += OnKeyDown;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.KeyDown -= OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // F2 only opens the panel and the game window owns that key: while the panel is open the
        // overlay has the focus, so the close key is Escape instead.
        if (e.Key == Key.Escape && _modeService.UiModeEnabled)
        {
            _logger.Info("[Hotkey] Esc => disable UI mode");
            _modeService.DisableUiMode();
            e.Handled = true;
        }
    }
}

