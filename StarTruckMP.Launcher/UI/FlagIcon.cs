using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using StarTruckMP.Launcher.Localization;

namespace StarTruckMP.Launcher.UI;

/// <summary>
/// Language flags drawn from basic shapes. Windows emoji fonts render the regional
/// indicator pairs as plain "PL"/"GB" letters, so the header paints real flags.
/// </summary>
internal static class FlagIcon
{
    private static readonly IBrush WhiteBrush = new SolidColorBrush(Color.Parse("#F4F4F4"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#D6364B"));
    private static readonly IBrush BlueBrush = new SolidColorBrush(Color.Parse("#1B3A6B"));

    public static Control Create(AppLanguage language, double width = 22)
    {
        var height = Math.Round(width * 2 / 3);

        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };

        if (language == AppLanguage.Polish)
        {
            AddRect(canvas, 0, 0, width, height / 2, WhiteBrush);
            AddRect(canvas, 0, height / 2, width, height / 2, RedBrush);
        }
        else
        {
            // A compact Union Jack: blue field, white then red diagonals, then the cross.
            AddRect(canvas, 0, 0, width, height, BlueBrush);
            AddDiagonal(canvas, width, height, WhiteBrush, height * 0.30, reverse: false);
            AddDiagonal(canvas, width, height, WhiteBrush, height * 0.30, reverse: true);
            AddDiagonal(canvas, width, height, RedBrush, height * 0.13, reverse: false);
            AddDiagonal(canvas, width, height, RedBrush, height * 0.13, reverse: true);
            AddRect(canvas, width / 2 - width * 0.15, 0, width * 0.30, height, WhiteBrush);
            AddRect(canvas, 0, height / 2 - height * 0.22, width, height * 0.44, WhiteBrush);
            AddRect(canvas, width / 2 - width * 0.07, 0, width * 0.14, height, RedBrush);
            AddRect(canvas, 0, height / 2 - height * 0.09, width, height * 0.18, RedBrush);
        }

        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(2),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A4655")),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Child = canvas
        };
    }

    private static void AddRect(Canvas canvas, double x, double y, double width, double height, IBrush brush)
    {
        var rectangle = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush
        };

        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
    }

    private static void AddDiagonal(Canvas canvas, double width, double height, IBrush brush, double thickness, bool reverse)
    {
        canvas.Children.Add(new Line
        {
            StartPoint = reverse ? new Point(width, 0) : new Point(0, 0),
            EndPoint = reverse ? new Point(0, height) : new Point(width, height),
            Stroke = brush,
            StrokeThickness = thickness
        });
    }
}
