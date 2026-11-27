using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace StarTruckerMP.Overlay.Host.UI;

internal sealed class OverlayWindow : Window
{
    private readonly Image _image;
    private PixelPoint _lastPosition;
    private double _lastWidth;
    private double _lastHeight;
    private bool _hasBounds;
    private bool _closeAllowed;

    public OverlayWindow()
    {
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;

        // Showing a window normally activates it, and an overlay that took the foreground from the
        // game left the player on the title screen unable to press Space. The click-through HUD
        // never activates; an interactive page is activated explicitly once it is ready.
        ShowActivated = false;
        CanResize = false;
        IsHitTestVisible = true;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        Focusable = true;

        _image = new Image
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };

        Content = new Grid
        {
            Background = Brushes.Transparent,
            Children =
            {
                _image
            }
        };

        // The overlay belongs to the game for as long as the game runs. A close request from the
        // desktop (Alt+F4 aimed at the game while this window happens to be in front) used to end
        // this process, after which F2 had nothing left to talk to.
        Closing += (_, e) =>
        {
            if (!_closeAllowed)
                e.Cancel = true;
        };
    }

    /// <summary>Lets the window close for real, which only the end of the game asks for.</summary>
    public void AllowClose() => _closeAllowed = true;

    public Bitmap? FrameBitmap
    {
        get => _image.Source as Bitmap;
        set => _image.Source = value;
    }

    public void NotifyFrameUpdated()
    {
        _image.InvalidateVisual();
        (Content as Control)?.InvalidateVisual();
        InvalidateVisual();
    }

    public nint GetWindowHandle()
    {
        var handle = TryGetPlatformHandle();
        return handle == null ? 0 : handle.Handle;
    }

    public void UpdateBounds(int left, int top, double dipWidth, double dipHeight)
    {
        var position = new PixelPoint(left, top);
        var width = Math.Max(1d, dipWidth);
        var height = Math.Max(1d, dipHeight);

        // Re-applying the same bounds resizes and repaints the layered window, which shows up as
        // a blink every time the overlay is unhidden. Only a real change is applied.
        if (_hasBounds &&
            position == _lastPosition &&
            Math.Abs(width - _lastWidth) < 0.5 &&
            Math.Abs(height - _lastHeight) < 0.5)
            return;

        _hasBounds = true;
        _lastPosition = position;
        _lastWidth = width;
        _lastHeight = height;

        Position = position;
        Width = width;
        Height = height;
    }

    public Point ClampToClient(Point point)
    {
        var x = Math.Clamp(point.X, 0, Bounds.Width <= 0 ? 0 : Bounds.Width - 1);
        var y = Math.Clamp(point.Y, 0, Bounds.Height <= 0 ? 0 : Bounds.Height - 1);
        return new Point(x, y);
    }

    protected override Type StyleKeyOverride => typeof(Window);
}

