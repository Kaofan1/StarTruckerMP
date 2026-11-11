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

    /// <summary>
    /// Enabling the mode moves focus to the overlay window, and Windows then delivers the same
    /// key press to the newly focused window. Without this window a single F2 would open the panel
    /// and immediately close it again.
    /// </summary>
    private static readonly TimeSpan EchoGuard = TimeSpan.FromSeconds(2);

    private DateTime _modeEnabledAt = DateTime.MinValue;

    public HotkeyService(OverlayWindow window, OverlayModeService modeService, IOverlayLogger logger)
    {
        _window = window;
        _modeService = modeService;
        _logger = logger;
        _window.KeyDown += OnKeyDown;
        _modeService.UiModeChanged += OnUiModeChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.KeyDown -= OnKeyDown;
        _modeService.UiModeChanged -= OnUiModeChanged;
    }

    private void OnUiModeChanged(object? sender, bool enabled)
    {
        if (enabled)
            _modeEnabledAt = DateTime.UtcNow;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // While UI mode is off the overlay is click-through and the game window has focus, so the
        // game handles F2 itself. Reacting here as well toggled the mode twice for one key press.
        if (e.Key == Key.F2 && _modeService.UiModeEnabled)
        {
            if (DateTime.UtcNow - _modeEnabledAt < EchoGuard)
            {
                _logger.Info("[Hotkey] F2 ignored, the same press already opened the panel");
                e.Handled = true;
                return;
            }

            _logger.Info("[Hotkey] F2 => toggle UI mode");
            _modeService.ToggleUiMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _modeService.UiModeEnabled)
        {
            _logger.Info("[Hotkey] Esc => disable UI mode");
            _modeService.DisableUiMode();
            e.Handled = true;
        }
    }
}

