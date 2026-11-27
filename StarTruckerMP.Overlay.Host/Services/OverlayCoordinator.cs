using Avalonia.Threading;
using System.Text.Json;
using StarTruckerMP.Overlay.Browser;
using StarTruckerMP.Overlay.Core.Abstractions;
using StarTruckerMP.Overlay.Core.Ipc;
using StarTruckerMP.Overlay.Core.Models;
using StarTruckerMP.Overlay.Core.Services;
using StarTruckerMP.Overlay.Host.Interop;
using StarTruckerMP.Overlay.Host.UI;

namespace StarTruckerMP.Overlay.Host.Services;

internal sealed class OverlayCoordinator : IDisposable
{
    private readonly OverlayWindow _window;
    private readonly CefOverlayBrowser _browser;
    private readonly OverlayRenderer _renderer;
    private readonly OverlayModeService _modeService;
    private readonly GameWindowTracker _tracker;
    private readonly Win32WindowStyles _windowStyles;
    private readonly OverlayPipeServer _pipeServer;
    private readonly IOverlayLogger _logger;
    private readonly DispatcherTimer _clickThroughGuard;
    private const string DefaultStartupUrl = "about:blank";

    /// <summary>If a page never reports that it loaded, the overlay is shown anyway.</summary>
    private const int PageShowFallbackMs = 4000;

    private bool _disposed;
    private bool _overlayVisible;
    private bool _pendingShow;
    private string? _expectedUrl;
    private int _navigationGate;

    public OverlayCoordinator(
        OverlayWindow window,
        CefOverlayBrowser browser,
        OverlayRenderer renderer,
        OverlayModeService modeService,
        GameWindowTracker tracker,
        Win32WindowStyles windowStyles,
        OverlayPipeServer pipeServer,
        IOverlayLogger logger)
    {
        _window = window;
        _browser = browser;
        _renderer = renderer;
        _modeService = modeService;
        _tracker = tracker;
        _windowStyles = windowStyles;
        _pipeServer = pipeServer;
        _logger = logger;
        _browser.FrameReady += OnBrowserFrameReady;
        _browser.DocumentReady += OnBrowserDocumentReady;
        _modeService.UiModeChanged += OnUiModeChanged;
        _tracker.StateChanged += OnTrackerStateChanged;
        _tracker.GameExited += OnGameExited;

        // Avalonia rewrites the window styles every time it shows the window, and a page that is
        // click-through must never end up holding the mouse or the keyboard.
        _clickThroughGuard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clickThroughGuard.Tick += OnClickThroughGuardTick;
    }

    public void Start()
    {
        EnsureWindowHandle();
        var hwnd = _window.GetWindowHandle();
        _windowStyles.ApplyOverlayWindowStyle(hwnd);
        _windowStyles.ShowWithoutActivation(hwnd);

        _tracker.Start();
        ApplyWindowState(_tracker.CurrentState);

        _browser.Initialize(DefaultStartupUrl, _tracker.CurrentState.PixelWidth, _tracker.CurrentState.PixelHeight);
        ApplyUiMode(enabled: false);
        _pipeServer.Start();
        _clickThroughGuard.Start();

        _logger.Info($"[Overlay] Started. StartupUrl={DefaultStartupUrl}");
    }

    public Task HandleCommandAsync(OverlayCommand command)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            switch (command)
            {
                case SetInteractiveCommand interactive:
                    if (interactive.Enabled)
                        _modeService.EnableUiMode();
                    else
                        _modeService.DisableUiMode();
                    break;

                case NavigateCommand navigate:
                    BeginGatedNavigation(navigate.Url, () => _browser.Navigate(navigate.Url));
                    break;

                case NavigateHtmlCommand navigateHtml:
                    BeginGatedNavigation(null, () => _browser.NavigateToHtml(navigateHtml.Html));
                    break;

                case SetTokenCommand token:
                    _browser.SetSessionToken(token.Token);
                    break;

                case PostMessageCommand message:
                    _browser.PostGameMessageJson(message.Json);
                    break;

                case ShowOverlayCommand:
                    ShowOverlay();
                    break;

                case HideOverlayCommand:
                    HideOverlay();
                    break;

                case RunDiagnosticsCommand:
                    BeginGatedNavigation(DefaultStartupUrl, () => _browser.Navigate(DefaultStartupUrl));
                    _modeService.EnableUiMode();
                    break;

                case UnknownOverlayCommand unknown:
                    _logger.Warn($"[Overlay] Unknown command: {unknown.Raw}");
                    break;
            }
        }).GetTask();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _clickThroughGuard.Stop();
        _clickThroughGuard.Tick -= OnClickThroughGuardTick;
        _browser.FrameReady -= OnBrowserFrameReady;
        _browser.DocumentReady -= OnBrowserDocumentReady;
        _modeService.UiModeChanged -= OnUiModeChanged;
        _tracker.StateChanged -= OnTrackerStateChanged;
        _tracker.GameExited -= OnGameExited;
        _pipeServer.Dispose();
        _tracker.Dispose();
    }

    private void OnBrowserFrameReady(object? sender, BrowserFrameReadyEventArgs e)
    {
        Dispatcher.UIThread.Post(() => _renderer.Render(e));
    }

    private void OnBrowserDocumentReady(object? sender, DocumentReadyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_pendingShow)
                return;

            // A load from the previous navigation can finish after the next one started.
            if (_expectedUrl != null && !string.Equals(_expectedUrl, e.Url, StringComparison.OrdinalIgnoreCase))
                return;

            _pendingShow = false;
            _expectedUrl = null;
            ShowOverlay();

            if (_modeService.UiModeEnabled)
                FocusOverlay();
        });
    }

    /// <summary>
    /// Navigates without letting the empty document reach the screen: the window is hidden until
    /// the page reports that it finished loading. Without this the overlay showed a white frame
    /// between being switched on and the page being painted.
    /// </summary>
    private void BeginGatedNavigation(string? expectedUrl, Action start)
    {
        var gate = ++_navigationGate;
        _pendingShow = true;
        _expectedUrl = expectedUrl;

        if (_window.IsVisible)
            _window.Hide();

        start();

        _ = Task.Run(async () =>
        {
            await Task.Delay(PageShowFallbackMs).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (gate != _navigationGate || !_pendingShow)
                    return;

                _logger.Warn("[Overlay] The page did not report a load; showing it anyway.");
                _pendingShow = false;
                _expectedUrl = null;
                ShowOverlay();

                if (_modeService.UiModeEnabled)
                    FocusOverlay();
            });
        });
    }

    private void OnUiModeChanged(object? sender, bool enabled)
    {
        Dispatcher.UIThread.Post(() => ApplyUiMode(enabled));
    }

    private void OnTrackerStateChanged(object? sender, GameWindowState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyWindowState(state);
            _browser.Resize(state.PixelWidth, state.PixelHeight);
        });
    }

    private void OnGameExited(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logger.Warn("[Overlay] The game exited; closing the overlay.");
            _window.AllowClose();
            _window.Close();
        });
    }

    private void ApplyUiMode(bool enabled)
    {
        var hwnd = EnsureWindowHandle();
        if (enabled)
        {
            ShowOverlay();
            _windowStyles.EnsureTopmost(hwnd);
            _windowStyles.SetClickThrough(hwnd, enabled: false);

            // While a page loads the window is hidden; OnBrowserDocumentReady focuses it once shown.
            if (!_pendingShow)
                FocusOverlay();

            _logger.Info("[Mode] UI mode ON => overlay captures input");
            return;
        }

        _browser.SendFocusEvent(false);
        _windowStyles.SetClickThrough(hwnd, enabled: true);
        _windowStyles.FocusGameWindow(_tracker.CurrentState.Handle);
        _logger.Info("[Mode] UI mode OFF => overlay is click-through and the game regains input");
    }

    private void ApplyWindowState(GameWindowState state)
    {
        _window.UpdateBounds(state.Left, state.Top, state.DipWidth, state.DipHeight);

        // A page is loading: the window stays hidden until the new document is ready, even when
        // the game window changes size in the meantime.
        if (_pendingShow)
        {
            if (_window.IsVisible)
                _window.Hide();
            return;
        }

        if (!_overlayVisible || state.IsMinimized || !state.IsVisible)
        {
            if (_window.IsVisible)
                _window.Hide();
            return;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
            var handle = EnsureWindowHandle();
            _windowStyles.ShowWithoutActivation(handle);
            _windowStyles.EnsureTopmost(handle);

            // Showing the window made Avalonia write its own extended style, which drops the
            // click-through bits; they have to be set again before the window is used.
            if (_modeService.UiModeEnabled)
                _windowStyles.SetClickThrough(handle, enabled: false);
            else
                _windowStyles.EnsureClickThrough(handle);

            // A click-through page draws state and nothing else, so the game keeps the keyboard
            // even while the HUD is on screen instead of waiting for a click to win it back.
            if (!_modeService.UiModeEnabled)
                _windowStyles.FocusGameWindow(state.Handle);
        }
    }

    /// <summary>
    /// Keeps the HUD click-through for as long as it is on screen. The window is repaired as soon
    /// as a style rewrite is noticed, and an overlay that somehow got the foreground hands it back
    /// to the game, which needs it for the truck and for F2.
    /// </summary>
    private void OnClickThroughGuardTick(object? sender, EventArgs e)
    {
        if (_disposed || !_overlayVisible || _modeService.UiModeEnabled)
            return;

        var hwnd = _window.GetWindowHandle();
        if (hwnd == 0)
            return;

        var repaired = _windowStyles.EnsureClickThrough(hwnd);
        var gameHandle = _tracker.CurrentState.Handle;

        // Only when the overlay itself holds the foreground: the BepInEx console has to stay
        // usable even while the HUD is visible.
        if (repaired || _windowStyles.ForegroundWindow() == hwnd)
            _windowStyles.FocusGameWindow(gameHandle);
    }

    private void ShowOverlay()
    {
        _overlayVisible = true;

        // The in-game HUD is click-through, so it never passes through ApplyUiMode. Re-assert the
        // topmost flag here too, otherwise a full screen game can cover the panel it just opened.
        var hwnd = _window.GetWindowHandle();
        if (hwnd != 0)
            _windowStyles.EnsureTopmost(hwnd);

        // The page is still loading; the window appears from the load notification instead.
        if (_pendingShow)
            return;

        ApplyWindowState(_tracker.CurrentState);

        // ApplyWindowState only touches the style when the window was hidden. A HUD that was
        // already on screen still needs the click-through bits after another show request.
        if (!_modeService.UiModeEnabled)
        {
            var handle = _window.GetWindowHandle();
            if (handle != 0)
                _windowStyles.EnsureClickThrough(handle);
        }

        _logger.Info("[Overlay] SHOW");
    }

    private void HideOverlay()
    {
        _overlayVisible = false;
        _pendingShow = false;
        _expectedUrl = null;
        _navigationGate++;

        if (_window.IsVisible)
            _window.Hide();

        // F2 closes the HUD while the game keeps playing, so the game takes the keyboard back
        // immediately instead of waiting for a click on its window.
        if (!_modeService.UiModeEnabled)
            _windowStyles.FocusGameWindow(_tracker.CurrentState.Handle);

        _logger.Info("[Overlay] HIDE");
    }

    private void FocusOverlay()
    {
        var hwnd = EnsureWindowHandle();
        _browser.SendFocusEvent(true);
        _window.Activate();
        _window.Focus();
        _windowStyles.FocusWindow(hwnd);
    }

    private nint EnsureWindowHandle()
    {
        var hwnd = _window.GetWindowHandle();
        if (hwnd == 0)
            throw new InvalidOperationException("Could not get the overlay window HWND.");
        return hwnd;
    }
}

