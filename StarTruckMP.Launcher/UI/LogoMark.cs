using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace StarTruckMP.Launcher.UI;

/// <summary>
/// The launcher mark: a hauler seen from the side with a star over the cab, drawn from
/// shapes so it stays sharp at any size (header tile, window icon) without image assets.
/// Everything is laid out on a 48x48 canvas and scaled by the caller.
/// </summary>
internal static class LogoMark
{
    private static readonly IBrush Amber = new SolidColorBrush(LauncherTheme.AmberColor);
    private static readonly IBrush Glass = new SolidColorBrush(Color.Parse("#22F2A33C"));
    private static readonly IBrush Void = new SolidColorBrush(Color.Parse("#0B121B"));

    public static Control Create(double size)
    {
        var canvas = new Canvas { Width = 48, Height = 48 };

        // Trailer body.
        canvas.Children.Add(new Rectangle
        {
            Width = 23,
            Height = 13,
            RadiusX = 2,
            RadiusY = 2,
            Stroke = Amber,
            StrokeThickness = 2,
            Fill = Glass,
            [Canvas.LeftProperty] = 3,
            [Canvas.TopProperty] = 17
        });

        // Cab, sloping to the windshield.
        canvas.Children.Add(new ShapePath
        {
            Data = Geometry.Parse("M 28,22 L 36,22 L 41,27 L 41,31 L 28,31 Z"),
            Stroke = Amber,
            StrokeThickness = 2,
            StrokeJoin = PenLineJoin.Round,
            Fill = Glass
        });

        // Twin rear wheels and the steer wheel.
        AddWheel(canvas, 9.5);
        AddWheel(canvas, 21);
        AddWheel(canvas, 34.5);

        // Star above the cab.
        canvas.Children.Add(new ShapePath
        {
            Data = Geometry.Parse("M 39,6 L 40.4,10.1 L 44,11.5 L 40.4,12.9 L 39,17 L 37.6,12.9 L 34,11.5 L 37.6,10.1 Z"),
            Fill = Amber
        });

        // Road line.
        canvas.Children.Add(new Rectangle
        {
            Width = 42,
            Height = 1.6,
            RadiusX = 0.8,
            RadiusY = 0.8,
            Fill = Amber,
            Opacity = 0.45,
            [Canvas.LeftProperty] = 3,
            [Canvas.TopProperty] = 38
        });

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = canvas
        };
    }

    private static void AddWheel(Canvas canvas, double centerX)
    {
        var wheel = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Void,
            Stroke = Amber,
            StrokeThickness = 1.6
        };

        Canvas.SetLeft(wheel, centerX - 3.5);
        Canvas.SetTop(wheel, 30.5);
        canvas.Children.Add(wheel);
    }
}
