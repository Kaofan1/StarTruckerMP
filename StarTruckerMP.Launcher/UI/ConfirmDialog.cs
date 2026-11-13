using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StarTruckerMP.Launcher.UI;

/// <summary>
/// Modal yes/no window for actions that delete files. It returns <c>true</c> only when the player
/// presses the confirm button, so closing it with the title bar button never deletes anything.
/// </summary>
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmText, string cancelText)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = LauncherTheme.PanelBrush;

        var confirm = LauncherTheme.MakeButton(confirmText, ActionVariant.Accent);
        var cancel = LauncherTheme.MakeButton(cancelText, ActionVariant.Secondary);

        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) => Close(true);

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        Grid.SetColumn(confirm, 1);

        Content = new Border
        {
            Background = LauncherTheme.PanelBrush,
            BorderBrush = LauncherTheme.EdgeBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 20),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontFamily = LauncherTheme.DisplayFont,
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = LauncherTheme.AmberBrush
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontFamily = LauncherTheme.BodyFont,
                        FontSize = 12.5,
                        Foreground = LauncherTheme.TextBrush,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 19
                    },
                    buttons
                }
            }
        };
    }

    /// <summary>Shows the dialog over the launcher and reports whether the player confirmed.</summary>
    public static Task<bool> AskAsync(Window owner, string title, string message, string confirmText, string cancelText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText, cancelText);
        return dialog.ShowDialog<bool>(owner);
    }
}
