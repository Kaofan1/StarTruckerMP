using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using StarTruckerMP.Launcher.Localization;
using StarTruckerMP.Launcher.Services;
using StarTruckerMP.Launcher.UI;

namespace StarTruckerMP.Launcher;

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
