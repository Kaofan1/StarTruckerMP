using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StarTruckMP.Launcher.Localization;
using StarTruckMP.Launcher.Models;
using StarTruckMP.Launcher.Services;

namespace StarTruckMP.Launcher.UI;

internal sealed class MainWindow : Window
{
    private readonly LauncherLogger _log = new();
    private readonly SettingsStore _store = new();
    private readonly ModInstaller _installer;
    private readonly LauncherSettings _settings;

    private GameInstall? _install;
    private bool _busy;
    private bool _refreshingServerList;

    private Border? _statusDot;

    private readonly TextBlock _gamePathText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12.5,
        Foreground = LauncherTheme.TextBrush
    };

    private readonly TextBlock _modStatusText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12.5,
        Foreground = LauncherTheme.MutedBrush
    };

    private readonly TextBlock _statusText = new()
    {
        Text = Strings.Standby,
        TextWrapping = TextWrapping.NoWrap,
        Foreground = LauncherTheme.MutedBrush,
        FontSize = 11.5,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _versionText = new()
    {
        Foreground = LauncherTheme.MutedBrush,
        FontSize = 11.5,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    private readonly Button _updateButton = LauncherTheme.MakeButton(
        "Update available",
        ActionVariant.Ghost,
        stretch: false);

    private readonly Button _languageButton = LauncherTheme.MakeButton(
        "English",
        ActionVariant.Ghost,
        stretch: false);

    private readonly TextBlock _serverSummaryText = new()
    {
        Text = Strings.NoServerSelected,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        FontFamily = LauncherTheme.DisplayFont,
        FontWeight = FontWeight.SemiBold,
        Foreground = LauncherTheme.TextBrush
    };

    private readonly TextBox _logBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = LauncherTheme.MonoFont,
        FontSize = 11.5,
        Height = 110,
        Background = LauncherTheme.FieldBrush,
        Foreground = LauncherTheme.LogBrush,
        BorderBrush = LauncherTheme.EdgeBrush,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 8)
    };

    private readonly Button _detectButton = LauncherTheme.MakeButton(
        Strings.DetectGame,
        ActionVariant.Secondary,
        stretch: false);

    private readonly Button _browseButton = LauncherTheme.MakeButton(
        Strings.ChooseFolder,
        ActionVariant.Secondary,
        stretch: false);

    private readonly Button _installButton = LauncherTheme.MakeButton(
        Strings.InstallMod,
        ActionVariant.Accent);

    private readonly Button _openGameFolderButton = LauncherTheme.MakeButton(
        Strings.OpenGameFolder,
        ActionVariant.Ghost);

    private readonly Button _openConfigButton = LauncherTheme.MakeButton(
        Strings.OpenConfig,
        ActionVariant.Ghost);

    private readonly Button _playButton = LauncherTheme.MakeButton(
        Strings.Play,
        ActionVariant.Primary,
        fontSize: 17,
        padding: new Thickness(24, 13));

    private readonly ComboBox _serverCombo = LauncherTheme.StyleCombo(new ComboBox
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinWidth = 240
    });

    private readonly Button _addServerButton = LauncherTheme.MakeButton(
        Strings.Add,
        ActionVariant.Secondary,
        stretch: false);

    private readonly Button _removeServerButton = LauncherTheme.MakeButton(
        Strings.Remove,
        ActionVariant.Ghost,
        stretch: false);

    private readonly Button _applyServerButton = LauncherTheme.MakeButton(
        Strings.SaveServer,
        ActionVariant.Accent);

    private readonly TextBox _nameBox = LauncherTheme.StyleInput(new TextBox
    {
        Watermark = Strings.ServerNameWatermark
    });

    private readonly TextBox _addressBox = LauncherTheme.StyleInput(new TextBox
    {
        Watermark = Strings.HostWatermark
    });

    private readonly TextBox _portBox = LauncherTheme.StyleInput(new TextBox
    {
        Watermark = Strings.PortWatermark,
        Width = 110,
        HorizontalAlignment = HorizontalAlignment.Left
    });

    private readonly TextBox _microphoneBox = LauncherTheme.StyleInput(new TextBox
    {
        Watermark = Strings.MicrophoneWatermark
    });

    private readonly CheckBox _steamCheck = LauncherTheme.StyleCheck(new CheckBox
    {
        Content = Wrap(Strings.LaunchThroughSteam)
    });

    private readonly CheckBox _sslCheck = LauncherTheme.StyleCheck(new CheckBox
    {
        Content = Wrap(Strings.IgnoreSsl)
    });

    private readonly CheckBox _closeCheck = LauncherTheme.StyleCheck(new CheckBox
    {
        Content = Wrap(Strings.CloseAfterLaunch)
    });

    public MainWindow(LauncherLogger? log = null)
    {
        if (log is not null)
            _log = log;

        _settings = _store.Load();
        Strings.Set(Strings.Parse(_settings.Language));
        _installer = new ModInstaller(_log);

        Title = "Star Trucker Multiplayer";
        Width = 1120;
        Height = 1000;
        MinWidth = 940;
        MinHeight = 760;
        RestoreSavedSize();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = LauncherTheme.SpaceBrush;

        _steamCheck.IsChecked = _settings.LaunchThroughSteam;
        _sslCheck.IsChecked = _settings.IgnoreSslValidation;
        _closeCheck.IsChecked = _settings.CloseLauncherAfterLaunch;
        _microphoneBox.Text = _settings.MicrophoneDeviceName;
        _versionText.Text = $"Launcher v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}  ·  github.com/Kaofan1/StarTruckerMP";

        Content = BuildLayout();

        var history = _log.Snapshot();
        if (history.Count > 0)
            _logBox.Text = string.Join(Environment.NewLine, history.Skip(Math.Max(0, history.Count - 300)));
        UpdateServerSummary();

        WireEvents();

        _updateButton.IsVisible = false;
        _updateButton.HorizontalAlignment = HorizontalAlignment.Right;
        _updateButton.Margin = new Thickness(0, 2, 0, 0);
        _updateButton.FontSize = 11.5;
        _updateButton.Foreground = LauncherTheme.AmberBrush;
        _updateButton.Click += (_, _) => OpenUpdatePage();

        _languageButton.HorizontalAlignment = HorizontalAlignment.Right;
        _languageButton.Margin = new Thickness(0, 0, 0, 2);
        _languageButton.Flyout = BuildLanguageMenu();
        UpdateLanguageButton();
        _log.Log(Strings.LanguageActive(Strings.Current));

        _log.LineWritten += OnLogLine;
        Opened += (_, _) =>
        {
            RestoreSavedPosition();
            FitToWorkingArea();
            DetectGame();
            _ = CheckForUpdatesAsync();
        };
        Closing += (_, _) => SaveSettings();
    }

    private static TextBlock Wrap(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12.5,
        Foreground = LauncherTheme.MutedBrush,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Control BuildLayout()
    {
        var header = BuildHeader();
        var body = BuildBody();

        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        shell.Children.Add(header);
        shell.Children.Add(body);
        Grid.SetRow(body, 1);

        // The starfield is the window backdrop; every panel above it paints its
        // own surface, so no extra blending is needed between them.
        var root = new Grid();
        root.Children.Add(new StarfieldPanel());
        root.Children.Add(shell);

        return root;
    }

    private Control BuildHeader()
    {
        var mark = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(10),
            Background = LauncherTheme.FieldBrush,
            BorderBrush = LauncherTheme.AmberBrush,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "ST",
                FontFamily = LauncherTheme.DisplayFont,
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Foreground = LauncherTheme.AmberBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var brand = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "STAR TRUCKER MULTIPLAYER",
                    FontFamily = LauncherTheme.DisplayFont,
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = LauncherTheme.AmberBrush
                },
                new TextBlock
                {
                    Text = "Star Trucker Multiplayer",
                    FontFamily = LauncherTheme.DisplayFont,
                    FontSize = 23,
                    FontWeight = FontWeight.Bold,
                    Foreground = LauncherTheme.TextBrush
                },
                new TextBlock
                {
                    Text = Strings.Subtitle,
                    FontSize = 11.5,
                    Foreground = LauncherTheme.MutedBrush
                }
            }
        };

        var brandRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children = { mark, brand }
        };

        var statusDot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(4),
            Background = LauncherTheme.TealBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        _statusDot = statusDot;

        var pill = new Border
        {
            Background = LauncherTheme.FieldBrush,
            BorderBrush = LauncherTheme.EdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { statusDot, _statusText }
            }
        };

        var statusColumn = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _languageButton, pill, _versionText, _updateButton }
        };

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        layout.Children.Add(brandRow);
        layout.Children.Add(statusColumn);
        Grid.SetColumn(statusColumn, 1);

        return new Border
        {
            Background = LauncherTheme.HeaderBrush,
            BorderBrush = LauncherTheme.EdgeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 16),
            Child = layout
        };
    }

    private Control BuildBody()
    {
        var left = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                BuildInstallCard(),
                BuildServerCard(),
                BuildLaunchCard()
            }
        };

        var leftScroll = new ScrollViewer
        {
            Content = left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var right = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(12, 0, 0, 0),
            Children =
            {
                BuildConnectCard(),
                BuildModCard(),
                BuildShortcutsCard()
            }
        };

        var rightScroll = new ScrollViewer
        {
            Content = right,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340") };
        split.Children.Add(leftScroll);
        split.Children.Add(rightScroll);
        Grid.SetColumn(rightScroll, 1);

        var log = BuildLogCard();
        log.Margin = new Thickness(0, 14, 0, 0);

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(20, 18)
        };

        body.Children.Add(split);
        body.Children.Add(log);
        Grid.SetRow(log, 1);

        return body;
    }

    private Control BuildInstallCard()
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _detectButton, _browseButton }
        };

        // Path and buttons share one row so the card stays short enough to fit the window.
        actions.VerticalAlignment = VerticalAlignment.Center;
        actions.Margin = new Thickness(12, 0, 0, 0);

        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        pathRow.Children.Add(_gamePathText);
        pathRow.Children.Add(actions);
        Grid.SetColumn(actions, 1);

        return LauncherTheme.Card(
            Strings.CardGame,
            "Steam · appid 2380050",
            pathRow,
            LauncherTheme.Caption(Strings.GameCaption));
    }

    private Control BuildServerCard()
    {
        _addServerButton.Margin = new Thickness(8, 0, 0, 0);
        _removeServerButton.Margin = new Thickness(8, 0, 0, 0);

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        toolbar.Children.Add(_serverCombo);
        toolbar.Children.Add(_addServerButton);
        toolbar.Children.Add(_removeServerButton);
        Grid.SetColumn(_addServerButton, 1);
        Grid.SetColumn(_removeServerButton, 2);

        // Name and host sit side by side, and the port keeps the save button company:
        // three short rows instead of five keep the card inside the window height.
        var nameHostRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        nameHostRow.Children.Add(LauncherTheme.Field(Strings.FieldName, _nameBox));

        var hostField = LauncherTheme.Field(Strings.FieldHost, _addressBox);
        hostField.Margin = new Thickness(12, 0, 0, 0);
        nameHostRow.Children.Add(hostField);
        Grid.SetColumn(hostField, 1);

        var portRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        portRow.Children.Add(LauncherTheme.Field(Strings.FieldPort, _portBox));

        _applyServerButton.Margin = new Thickness(12, 0, 0, 0);
        portRow.Children.Add(_applyServerButton);
        Grid.SetColumn(_applyServerButton, 1);

        return LauncherTheme.Card(
            Strings.CardServers,
            null,
            toolbar,
            nameHostRow,
            portRow,
            LauncherTheme.Caption(Strings.ServerCaption));
    }

    private Control BuildLaunchCard()
    {
        return LauncherTheme.Card(
            Strings.CardLaunch,
            null,
            _steamCheck,
            _sslCheck,
            _closeCheck,
            LauncherTheme.Field(Strings.FieldMicrophone, _microphoneBox));
    }

    private Control BuildConnectCard()
    {
        return LauncherTheme.Card(
            Strings.CardConnection,
            null,
            _serverSummaryText,
            _playButton,
            LauncherTheme.Caption(Strings.PlayCaption));
    }

    private Control BuildModCard()
    {
        return LauncherTheme.Card(
            Strings.CardModStatus,
            Strings.ModNote,
            _modStatusText,
            _installButton);
    }

    private Control BuildShortcutsCard()
    {
        // Two equal buttons on one row keep the column rhythm tight; stacked full-width bars
        // left a large gap under the heading and made the card the tallest in the column.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        row.Children.Add(_openGameFolderButton);

        _openConfigButton.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(_openConfigButton);
        Grid.SetColumn(_openConfigButton, 1);

        return LauncherTheme.Card(
            Strings.CardShortcuts,
            null,
            row,
            LauncherTheme.Caption(Strings.ShortcutsCaption));
    }

    private Control BuildLogCard()
    {
        return LauncherTheme.Card(
            Strings.CardLog,
            null,
            _logBox,
            LauncherTheme.Caption(LauncherLogger.LogPath, 10.5));
    }

    private void WireEvents()
    {
        _detectButton.Click += (_, _) => DetectGame();
        _browseButton.Click += async (_, _) => await BrowseGameAsync();
        _installButton.Click += async (_, _) => await InstallModAsync();
        _playButton.Click += async (_, _) => await PlayAsync();
        _openGameFolderButton.Click += (_, _) => OpenPath(_install?.Path);
        _openConfigButton.Click += (_, _) => OpenPath(_install is null ? null : PluginConfigWriter.GetConfigPath(_install.Path));
        _addServerButton.Click += (_, _) => AddServer();
        _removeServerButton.Click += (_, _) => RemoveServer();
        _applyServerButton.Click += (_, _) => ApplyServerEdits();
        _serverCombo.SelectionChanged += (_, _) => LoadSelectedServerIntoEditor();
    }

    private void DetectGame()
    {
        _install = GameLocator.Locate(_settings.GamePath);

        if (_install is null)
        {
            _settings.GamePath = null;
            _gamePathText.Text = Strings.GameNotFoundHint;
            _gamePathText.Foreground = LauncherTheme.AmberBrush;
            _log.Log(Strings.GameNotDetected);
        }
        else
        {
            _settings.GamePath = _install.Path;
            _gamePathText.Text = _install.Path;
            _gamePathText.Foreground = LauncherTheme.TextBrush;
            _log.Log(Strings.GameDetected(_install.Path));

            if (_install.Warning is not null)
                _log.Log(Strings.WarningPrefix + _install.Warning);
        }

        RefreshModState();
        RefreshServerList();
        SaveSettings();
    }

    private async System.Threading.Tasks.Task BrowseGameAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the Star Trucker folder (the one with Star Trucker.exe)",
            AllowMultiple = false
        });

        var localPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
            return;

        var install = GameInstall.FromPath(localPath);
        if (install is null)
        {
            _log.Log(Strings.FolderHasNoGame);
            return;
        }

        _settings.GamePath = install.Path;
        _store.Save(_settings);
        DetectGame();
    }

    private async System.Threading.Tasks.Task InstallModAsync()
    {
        if (_install is null || _busy)
            return;

        SetBusy(true, Strings.ModInstalling);

        var gamePath = _install.Path;
        var progress = new Progress<string>(message => _log.Log(message));

        try
        {
            // The loader is a separate, optional component: the client installer never downloads
            // BepInEx, and the launcher itself works without either of them.
            await System.Threading.Tasks.Task.Run(async () =>
            {
                await StarTruckMP.Launcher.Mod.BepInExRuntime.InstallAsync(
                    gamePath,
                    StarTruckMP.Launcher.Mod.BepInExRuntime.DefaultPayloadRoot,
                    progress,
                    System.Threading.CancellationToken.None);

                await _installer.InstallAsync(gamePath, progress, System.Threading.CancellationToken.None);
            });
            _log.Log(Strings.ModInstallCompleted);
        }
        catch (Exception exception)
        {
            _log.Log(Strings.ModInstallFailed(exception.Message));
        }
        finally
        {
            SetBusy(false, string.Empty);
            RefreshModState();
        }
    }

    /// <summary>Reopens the launcher with the size it was left at.</summary>
    private void RestoreSavedSize()
    {
        if (_settings.WindowWidth is not double width || _settings.WindowHeight is not double height)
            return;

        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
    }

    /// <summary>
    /// Reopens the launcher where it was left, but only when that spot is still on a screen —
    /// a monitor unplugged since the last run must not leave the window off the desktop.
    /// </summary>
    private void RestoreSavedPosition()
    {
        if (_settings.WindowX is not int x || _settings.WindowY is not int y)
            return;

        try
        {
            var scale = RenderScaling;
            var rect = new PixelRect(
                x,
                y,
                (int)Math.Round(Width * scale),
                (int)Math.Round(Height * scale));

            var onScreen = Screens.All.Any(screen =>
            {
                var area = screen.WorkingArea;
                return rect.X < area.Right && rect.Right > area.X && rect.Y < area.Bottom && rect.Bottom > area.Y;
            });

            if (!onScreen)
                return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        catch
        {
            // Placement is a convenience: fall back to the default centred window.
        }
    }

    /// <summary>
    /// The window is sized for the full dashboard; on a screen with a smaller work area it
    /// shrinks to fit instead of hanging off the desktop, and the columns scroll again.
    /// </summary>
    private void FitToWorkingArea()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
                return;

            var scale = RenderScaling;
            var area = screen.WorkingArea;
            var available = area.Height / scale;

            if (Height > available - 40)
                Height = Math.Max(MinHeight, available - 40);

            // Keep the title bar reachable when the window is as tall as the work area.
            var physicalHeight = (int)Math.Round(Height * scale);
            var maxTop = area.Y + area.Height - physicalHeight - 4;
            var top = Math.Max(area.Y, Math.Min(Position.Y, maxTop));

            if (top != Position.Y)
                Position = new PixelPoint(Position.X, top);
        }
        catch
        {
            // Screen information is best effort; the fixed size is a safe fallback.
        }
    }

    private MenuFlyout BuildLanguageMenu()
    {
        var menu = new MenuFlyout();

        foreach (var language in new[] { AppLanguage.English, AppLanguage.Polish })
        {
            var isCurrent = Strings.Current == language;

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    FlagIcon.Create(language),
                    new TextBlock
                    {
                        Text = Strings.LanguageName(language),
                        FontSize = 12.5,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };

            if (isCurrent)
                header.Children.Add(new TextBlock
                {
                    Text = "✓",
                    FontSize = 12,
                    Foreground = LauncherTheme.AmberBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });

            var item = new MenuItem { Header = header };

            var target = language;
            item.Click += (_, _) => SwitchLanguage(target);
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>Header button always shows the flag and name of the selected language.</summary>
    private void UpdateLanguageButton()
    {
        if (_languageButton.Content is not Border surface)
            return;

        surface.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                FlagIcon.Create(Strings.Current),
                new TextBlock
                {
                    Text = Strings.LanguageName(Strings.Current),
                    FontSize = 12.5,
                    FontFamily = LauncherTheme.DisplayFont,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = LauncherTheme.MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = "▾",
                    FontSize = 10,
                    Foreground = LauncherTheme.MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    /// <summary>
    /// Every label is built from <see cref="Strings"/> at construction time, so a language
    /// change recreates the window and hands it the same logger to keep the session log.
    /// </summary>
    private void SwitchLanguage(AppLanguage language)
    {
        if (Strings.Current == language)
            return;

        Strings.Set(language);
        _settings.Language = Strings.StorageCode(language);
        _store.Save(_settings);
        _log.Log(Strings.LanguageSwitched(language));

        // The replacement window restores geometry from the settings, so store it first.
        SaveSettings();

        var replacement = new MainWindow(_log);
        replacement.Show();
        Close();
    }

    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateChecker.FindUpdateAsync(System.Threading.CancellationToken.None);

            if (update is null)
            {
                _log.Log(Strings.UpToDate(UpdateChecker.CurrentVersion.ToString(3)));
                return;
            }

            _updateButton.Tag = update.PageUrl;
            _updateButton.Content = Strings.UpdateAvailable(update.Name);
            _updateButton.IsVisible = true;
            _log.Log(Strings.UpdateFound(update.Name, update.Tag, update.PageUrl));
        }
        catch (Exception exception)
        {
            // A failed update check must never get in the way of launching the game.
            _log.Log(Strings.UpdateCheckSkipped(exception.Message));
        }
    }

    private void OpenUpdatePage()
    {
        var url = _updateButton.Tag as string ?? UpdateChecker.ReleasesPageUrl;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            _log.Log(Strings.ReleasePageOpened(url));
        }
        catch (Exception exception)
        {
            _log.Log(Strings.ReleasePageFailed(exception.Message));
        }
    }

    private async System.Threading.Tasks.Task PlayAsync()
    {
        if (_install is null || _busy)
            return;

        var server = GetSelectedServer();
        if (server is null)
        {
            _log.Log(Strings.AddServerFirst);
            return;
        }

        if (string.IsNullOrWhiteSpace(server.Address))
        {
            _log.Log(Strings.ServerHasNoAddress);
            return;
        }

        if (!int.TryParse(server.Port, out var port) || port is < 1 or > 65535)
        {
            _log.Log(Strings.InvalidPort(server.Port));
            return;
        }

        if (!ModInstaller.HasBepInEx(_install.Path) || !ModInstaller.HasPlugin(_install.Path))
        {
            _log.Log(Strings.ModMissingHint);
        }

        try
        {
            PluginConfigWriter.Apply(
                _install.Path,
                server.Address.Trim(),
                port.ToString(),
                _sslCheck.IsChecked == true,
                _microphoneBox.Text?.Trim() ?? string.Empty);

            _log.Log(Strings.ServerSet(server.Address.Trim(), port.ToString()));

            var throughSteam = _steamCheck.IsChecked != false;
            GameLauncher.Launch(_install, throughSteam);
            _log.Log(throughSteam ? Strings.LaunchedThroughSteam : Strings.LaunchedDirectly);

            if (_closeCheck.IsChecked == true)
            {
                await System.Threading.Tasks.Task.Delay(1500);
                Close();
            }
        }
        catch (Exception exception)
        {
            _log.Log(Strings.LaunchFailed(exception.Message));
        }
    }

    private void RefreshModState()
    {
        if (_install is null)
        {
            _modStatusText.Text = Strings.ModUnknown;
            _modStatusText.Foreground = LauncherTheme.MutedBrush;
        }
        else
        {
            var hasBepInEx = ModInstaller.HasBepInEx(_install.Path);
            var hasPlugin = ModInstaller.HasPlugin(_install.Path);
            var hasOverlay = ModInstaller.HasOverlay(_install.Path);
            var hasMonoBepInEx = ModInstaller.HasMonoBepInEx(_install.Path);

            if (hasBepInEx && hasPlugin && hasOverlay)
            {
                _modStatusText.Text = Strings.ModInstalled;
                _modStatusText.Foreground = LauncherTheme.TealBrush;
            }
            else if (hasMonoBepInEx)
            {
                _modStatusText.Text = Strings.ModMono;
                _modStatusText.Foreground = LauncherTheme.DangerBrush;
            }
            else if (!hasBepInEx && !hasPlugin)
            {
                _modStatusText.Text = Strings.ModNotInstalled;
                _modStatusText.Foreground = LauncherTheme.AmberBrush;
            }
            else
            {
                var missing = new List<string>();
                if (!hasBepInEx) missing.Add(Strings.MissingBepInEx);
                if (!hasPlugin) missing.Add(Strings.MissingClient);
                if (!hasOverlay) missing.Add(Strings.MissingOverlay);
                _modStatusText.Text = Strings.ModPartial(string.Join(", ", missing));
                _modStatusText.Foreground = LauncherTheme.AmberBrush;
            }
        }

        _installButton.IsEnabled = _install is not null && !_busy;
        _playButton.IsEnabled = _install is not null && !_busy;
        _openGameFolderButton.IsEnabled = _install is not null;
        _openConfigButton.IsEnabled = _install is not null;
    }

    private void RefreshServerList()
    {
        _refreshingServerList = true;

        var selected = GetSelectedServer();
        _serverCombo.ItemsSource = null;
        _serverCombo.ItemsSource = _settings.Servers;
        _serverCombo.ItemTemplate = new FuncDataTemplate<ServerEntry>((entry, _) =>
            new TextBlock { Text = entry?.ToString() ?? string.Empty });
        _serverCombo.SelectedItem = selected ?? _settings.Servers.FirstOrDefault();

        _refreshingServerList = false;
        LoadSelectedServerIntoEditor();
        UpdateServerSummary();
    }

    private void LoadSelectedServerIntoEditor()
    {
        if (_refreshingServerList)
            return;

        var server = GetSelectedServer();
        if (server is null)
            return;

        _nameBox.Text = server.Name;
        _addressBox.Text = server.Address;
        _portBox.Text = server.Port;
        _settings.SelectedServerId = server.Id;
        _store.Save(_settings);
        UpdateServerSummary();
    }

    private void UpdateServerSummary()
    {
        var server = GetSelectedServer();
        _serverSummaryText.Text = server is null
            ? Strings.NoServerSelected
            : $"{server.Name} · {server.Address}:{server.Port}";
    }

    private void AddServer()
    {
        var server = new ServerEntry { Name = Strings.NewServerName, Address = "127.0.0.1", Port = "7777" };
        _settings.Servers.Add(server);
        _settings.SelectedServerId = server.Id;

        _refreshingServerList = true;
        _serverCombo.ItemsSource = null;
        _serverCombo.ItemsSource = _settings.Servers;
        _serverCombo.SelectedItem = server;
        _refreshingServerList = false;

        LoadSelectedServerIntoEditor();
        _store.Save(_settings);
        _log.Log(Strings.ServerAdded);
    }

    private void RemoveServer()
    {
        var server = GetSelectedServer();
        if (server is null)
            return;

        if (_settings.Servers.Count <= 1)
        {
            _log.Log(Strings.KeepOneServer);
            return;
        }

        _settings.Servers.Remove(server);
        _settings.SelectedServerId = _settings.Servers[0].Id;
        RefreshServerList();
        _store.Save(_settings);
        _log.Log(Strings.ServerRemoved(server.Name));
    }

    private void ApplyServerEdits()
    {
        var server = GetSelectedServer();
        if (server is null)
            return;

        var address = _addressBox.Text?.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            _log.Log(Strings.AddressEmpty);
            return;
        }

        if (!int.TryParse(_portBox.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            _log.Log(Strings.PortRange);
            return;
        }

        server.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? address : _nameBox.Text!.Trim();
        server.Address = address;
        server.Port = port.ToString();

        RefreshServerList();
        _store.Save(_settings);
        _log.Log(Strings.ServerSaved(server.Name, server.Address, server.Port));
    }

    private ServerEntry? GetSelectedServer() =>
        _serverCombo.SelectedItem as ServerEntry ?? _settings.SelectedServer;

    private void SaveSettings()
    {
        _settings.LaunchThroughSteam = _steamCheck.IsChecked != false;
        _settings.IgnoreSslValidation = _sslCheck.IsChecked == true;
        _settings.CloseLauncherAfterLaunch = _closeCheck.IsChecked == true;
        _settings.MicrophoneDeviceName = _microphoneBox.Text?.Trim() ?? string.Empty;
        _settings.SelectedServerId = GetSelectedServer()?.Id ?? _settings.SelectedServerId;

        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowX = Position.X;
            _settings.WindowY = Position.Y;
        }

        _store.Save(_settings);
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _statusText.Text = string.IsNullOrWhiteSpace(status) ? Strings.Standby : status;

        if (_statusDot is not null)
            _statusDot.Background = busy ? LauncherTheme.AmberBrush : LauncherTheme.TealBrush;

        _installButton.IsEnabled = !busy && _install is not null;
        _playButton.IsEnabled = !busy && _install is not null;
        _detectButton.IsEnabled = !busy;
        _browseButton.IsEnabled = !busy;
    }

    private void OnLogLine(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var snapshot = _log.Snapshot();
            _logBox.Text = string.Join(Environment.NewLine, snapshot.Skip(Math.Max(0, snapshot.Count - 300)));
            _logBox.CaretIndex = _logBox.Text?.Length ?? 0;
        });
    }

    private void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _log.Log(Strings.NothingToOpen);
            return;
        }

        try
        {
            GameLauncher.OpenFolder(path);
        }
        catch (Exception exception)
        {
            _log.Log(Strings.CouldNotOpen(path, exception.Message));
        }
    }
}
