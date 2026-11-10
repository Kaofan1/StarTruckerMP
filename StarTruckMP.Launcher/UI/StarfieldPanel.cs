using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StarTruckMP.Launcher.UI;

/// <summary>
/// Decorative backdrop: deep space gradient, a warm nebula glow and a static
/// starfield. It is a plain drawing control with a fixed seed, so the window
/// looks identical on every launch and costs nothing per frame.
/// </summary>
internal sealed class StarfieldPanel : Control
{
    private const int StarCount = 160;
    private const int Seed = 2380050;

    private static readonly Star[] Stars = CreateStars();

    private static readonly IBrush Sky = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#05090F"), 0),
            new GradientStop(Color.Parse("#0A1220"), 0.55),
            new GradientStop(Color.Parse("#070C14"), 1)
        }
    };

    private static readonly IBrush Nebula = new RadialGradientBrush
    {
        Center = new RelativePoint(0.85, 0.02, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.85, 0.02, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#2EF2A33C"), 0),
            new GradientStop(Color.Parse("#00070C14"), 1)
        }
    };

    private static readonly IBrush CoolGlow = new RadialGradientBrush
    {
        Center = new RelativePoint(0.05, 0.95, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.05, 0.95, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#1F5BC8B0"), 0),
            new GradientStop(Color.Parse("#00070C14"), 1)
        }
    };

    private static readonly IBrush[] StarBrushes =
    {
        new SolidColorBrush(Color.Parse("#33F3E7D0")),
        new SolidColorBrush(Color.Parse("#66F3E7D0")),
        new SolidColorBrush(Color.Parse("#99F2A33C"))
    };

    public StarfieldPanel()
    {
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        context.FillRectangle(Sky, bounds);
        context.FillRectangle(Nebula, bounds);
        context.FillRectangle(CoolGlow, bounds);

        foreach (var star in Stars)
        {
            var x = star.X * bounds.Width;
            var y = star.Y * bounds.Height * 0.92;
            context.FillRectangle(StarBrushes[star.Tier], new Rect(x, y, star.Size, star.Size));
        }
    }

    private static Star[] CreateStars()
    {
        var random = new Random(Seed);
        var stars = new Star[StarCount];

        for (var i = 0; i < stars.Length; i++)
        {
            var tier = random.Next(0, 3);
            stars[i] = new Star(
                random.NextDouble(),
                random.NextDouble(),
                tier == 2 ? 2.0 : 1.0 + random.NextDouble() * 0.8,
                tier);
        }

        return stars;
    }

    private readonly struct Star
    {
        public Star(double x, double y, double size, int tier)
        {
            X = x;
            Y = y;
            Size = size;
            Tier = tier;
        }

        public double X { get; }

        public double Y { get; }

        public double Size { get; }

        public int Tier { get; }
    }
}
