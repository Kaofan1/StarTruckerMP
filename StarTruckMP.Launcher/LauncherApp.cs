using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using StarTruckMP.Launcher.Localization;
using StarTruckMP.Launcher.Services;
using StarTruckMP.Launcher.UI;

namespace StarTruckMP.Launcher;

internal sealed class LauncherApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Buttons and inputs are created in field initializers, which run before the window
        // constructor, so the language has to be selected before the window is built.
        var settings = new SettingsStore().Load();
        Strings.Set(Strings.Parse(settings.Language));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
