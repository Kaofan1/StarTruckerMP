using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace StarTruckMP.Launcher.UI;

/// <summary>
/// Button flavours used across the launcher.
/// </summary>
internal enum ActionVariant
{
    /// <summary>Amber gradient, used for the single main action (PLAY).</summary>
    Primary,

    /// <summary>Raised panel surface for ordinary actions.</summary>
    Secondary,

    /// <summary>Borderless, low emphasis action.</summary>
    Ghost,

    /// <summary>Outline in the brand amber, used for "install / repair".</summary>
    Accent
}

/// <summary>
/// Single source of truth for the launcher look: palette, type scale and the
/// small set of building blocks (panels, labels, fields, buttons) that the
/// window composes. Keeping colours here means a restyle never has to touch
/// layout code, and every screen shares the same cockpit-panel language.
/// </summary>
internal static class LauncherTheme
{
    // Star Trucker palette: deep space behind warm cockpit panels.
    public static readonly Color SpaceColor = Color.Parse("#070C14");
    public static readonly Color PanelColor = Color.Parse("#101823");
    public static readonly Color PanelRaisedColor = Color.Parse("#16202C");
    public static readonly Color FieldColor = Color.Parse("#0B121B");
    public static readonly Color EdgeColor = Color.Parse("#24303F");
    public static readonly Color CreamColor = Color.Parse("#F3E7D0");
    public static readonly Color MutedColor = Color.Parse("#93A1AF");
    public static readonly Color AmberColor = Color.Parse("#F2A33C");
    public static readonly Color EmberColor = Color.Parse("#D9662B");
    public static readonly Color InkColor = Color.Parse("#20140A");
    public static readonly Color TealColor = Color.Parse("#5BC8B0");
    public static readonly Color DangerColor = Color.Parse("#E2573F");
    public static readonly Color LogColor = Color.Parse("#C9D6E2");
    public static readonly Color HeaderColor = Color.Parse("#0A1119");

    public static readonly IBrush SpaceBrush = new SolidColorBrush(SpaceColor);
    public static readonly IBrush PanelBrush = new SolidColorBrush(PanelColor);
    public static readonly IBrush PanelRaisedBrush = new SolidColorBrush(PanelRaisedColor);
    public static readonly IBrush FieldBrush = new SolidColorBrush(FieldColor);
    public static readonly IBrush EdgeBrush = new SolidColorBrush(EdgeColor);
    public static readonly IBrush TextBrush = new SolidColorBrush(CreamColor);
    public static readonly IBrush MutedBrush = new SolidColorBrush(MutedColor);
    public static readonly IBrush AmberBrush = new SolidColorBrush(AmberColor);
    public static readonly IBrush EmberBrush = new SolidColorBrush(EmberColor);
    public static readonly IBrush TealBrush = new SolidColorBrush(TealColor);
    public static readonly IBrush DangerBrush = new SolidColorBrush(DangerColor);
    public static readonly IBrush LogBrush = new SolidColorBrush(LogColor);
    public static readonly IBrush HeaderBrush = new SolidColorBrush(HeaderColor);

    public static readonly FontFamily DisplayFont = new("Bahnschrift, Segoe UI");
    public static readonly FontFamily BodyFont = new("Segoe UI, Bahnschrift");
    public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    public static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    /// <summary>Panel heading: small, uppercase, amber.</summary>
    public static TextBlock PanelTitle(string text) => new()
    {
        Text = text,
        FontFamily = DisplayFont,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = AmberBrush,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>Small caption / helper text under a control.</summary>
    public static TextBlock Caption(string text, double size = 11, IBrush? brush = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? MutedBrush,
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>A label + input row with one fixed label column so every form lines up.</summary>
    public static Grid Field(string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*") };

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        row.Children.Add(input);
        Grid.SetColumn(input, 1);
        return row;
    }

    /// <summary>Rounded panel with an amber heading strip and a hairline divider.</summary>
    public static Border Card(string title, string? note, params Control[] children)
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(PanelTitle(title));

        if (!string.IsNullOrWhiteSpace(note))
        {
            var noteText = new TextBlock
            {
                Text = note,
                FontSize = 10.5,
                Foreground = MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            heading.Children.Add(noteText);
            Grid.SetColumn(noteText, 1);
        }

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(heading);
        content.Children.Add(new Border { Height = 1, Background = EdgeBrush, Margin = new Thickness(0, 1) });

        foreach (var child in children)
            content.Children.Add(child);

        return new Border
        {
            Background = PanelBrush,
            BorderBrush = EdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 16),
            Child = content
        };
    }

    public static TextBox StyleInput(TextBox input)
    {
        input.Background = FieldBrush;
        input.Foreground = TextBrush;
        input.BorderBrush = EdgeBrush;
        input.BorderThickness = new Thickness(1);
        input.MinHeight = 34;
        input.Padding = new Thickness(10, 7);
        input.FontSize = 12.5;
        input.FontFamily = BodyFont;
        return input;
    }

    public static ComboBox StyleCombo(ComboBox combo)
    {
        combo.Background = FieldBrush;
        combo.Foreground = TextBrush;
        combo.BorderBrush = EdgeBrush;
        combo.BorderThickness = new Thickness(1);
        combo.MinHeight = 34;
        combo.FontSize = 12.5;
        combo.FontFamily = BodyFont;
        return combo;
    }

    public static CheckBox StyleCheck(CheckBox check)
    {
        check.Foreground = MutedBrush;
        check.FontSize = 12.5;
        check.FontFamily = BodyFont;
        check.VerticalContentAlignment = VerticalAlignment.Center;
        return check;
    }

    /// <summary>
    /// Builds a button whose whole surface is one of our own borders.
    /// The Fluent theme paints its hover overlay behind the content, so an
    /// opaque inner surface keeps the palette exact while hover/pressed states
    /// stay under our control.
    /// </summary>
    public static Button MakeButton(
        string text,
        ActionVariant variant = ActionVariant.Secondary,
        Action? onClick = null,
        bool stretch = true,
        double fontSize = 12.5,
        Thickness? padding = null)
    {
        var (background, hover, foreground, border) = VariantBrushes(variant);

        var surface = new Border
        {
            Background = background,
            BorderBrush = border is null ? null : new SolidColorBrush(border.Value),
            BorderThickness = new Thickness(border is null ? 0 : 1),
            CornerRadius = new CornerRadius(8),
            Padding = padding ?? new Thickness(16, 9),
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = DisplayFont,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(foreground),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            }
        };

        var button = new Button
        {
            Content = surface,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = HandCursor,
            HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        button.PointerEntered += (_, _) =>
        {
            if (button.IsEnabled)
                surface.Background = hover;
        };

        button.PointerExited += (_, _) => surface.Background = background;

        button.PropertyChanged += (_, args) =>
        {
            if (args.Property == InputElement.IsEnabledProperty)
                surface.Opacity = button.IsEnabled ? 1 : 0.4;
        };

        if (onClick is not null)
            button.Click += (_, _) => onClick();

        return button;
    }

    private static (IBrush background, IBrush hover, Color foreground, Color? border) VariantBrushes(
        ActionVariant variant)
    {
        switch (variant)
        {
            case ActionVariant.Primary:
                return (
                    Gradient(AmberColor, EmberColor),
                    Gradient(Color.Parse("#FFB45B"), Color.Parse("#E9752F")),
                    InkColor,
                    null);

            case ActionVariant.Accent:
                return (
                    PanelRaisedBrush,
                    new SolidColorBrush(Color.Parse("#2A2318")),
                    AmberColor,
                    AmberColor);

            case ActionVariant.Ghost:
                return (
                    Brushes.Transparent,
                    PanelRaisedBrush,
                    MutedColor,
                    null);

            default:
                return (
                    PanelRaisedBrush,
                    new SolidColorBrush(Color.Parse("#1D2836")),
                    CreamColor,
                    EdgeColor);
        }
    }

    private static IBrush Gradient(Color from, Color to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(from, 0),
            new GradientStop(to, 1)
        }
    };
}
