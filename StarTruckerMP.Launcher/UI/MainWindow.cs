using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StarTruckerMP.Launcher.Localization;
using StarTruckerMP.Launcher.Models;
using StarTruckerMP.Launcher.Services;

namespace StarTruckerMP.Launcher.UI;

internal sealed class MainWindow : Window
{
    private readonly LauncherLogger _log = new();
    private readonly SettingsStore _store = new();
    private readonly ModInstaller _installer;
    private readonly LauncherSettings _settings;

    private GameInstall? _install;
    private bool _busy;
    private bool _refreshingServerList;
    private LauncherUpdate? _pendingUpdate;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private LocalServerStatus? _localServer;
    private readonly DispatcherTimer _serverTimer = new() { Interval = TimeSpan.FromSeconds(3) };

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

    private readonly TextBlock _loaderStatusText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12.5,
        Foreground = LauncherTheme.MutedBrush
    };

    private readonly TextBlock _localServerStatusText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12.5,
        Foreground = LauncherTheme.MutedBrush
    };

    private readonly Button _serverToggleButton = LauncherTheme.MakeButton(
        Strings.StartLocalServer,
        ActionVariant.Secondary);

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

    private readonly TextBlock _updateTitleText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        FontFamily = LauncherTheme.DisplayFont,
        FontWeight = FontWeight.SemiBold,
        Foreground = LauncherTheme.TextBrush
    };

    private readonly TextBlock _updateNotesText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Foreground = LauncherTheme.MutedBrush
    };

    private readonly Button _updateNowButton = LauncherTheme.MakeButton(
        Strings.UpdateNow,
        ActionVariant.Primary,
        stretch: false);

    private readonly Button _releasePageButton = LauncherTheme.MakeButton(
        Strings.ReleasePage,
        ActionVariant.Ghost,
        stretch: false);

    private Border? _updateCard;

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
        Strings.InstallAction,
        ActionVariant.Accent);

    private readonly Button _repairButton = LauncherTheme.MakeButton(
        Strings.RepairMod,
        ActionVariant.Secondary);

    private readonly Button _uninstallButton = LauncherTheme.MakeButton(
        Strings.UninstallAction,
        ActionVariant.Ghost);

    private readonly Button _installLoaderButton = LauncherTheme.MakeButton(
        Strings.InstallAction,
        ActionVariant.Accent);

    private readonly Button _repairLoaderButton = LauncherTheme.MakeButton(
        Strings.RepairLoader,
        ActionVariant.Secondary);

    private readonly Button _uninstallLoaderButton = LauncherTheme.MakeButton(
        Strings.UninstallAction,
        ActionVariant.Ghost);

    /// <summary>Shown while the local server runs, so its status page is one click away.</summary>
    private readonly Button _serverPageButton = LauncherTheme.MakeButton(
        LocalServer.StatusPageUrl,
        ActionVariant.Secondary,
        stretch: false,
        fontSize: 12);

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

    private readonly ComboBox _microphoneCombo = LauncherTheme.StyleCombo(new ComboBox
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinWidth = 200
    });

    private readonly Button _refreshMicrophoneButton = LauncherTheme.MakeButton(
        Strings.MicRefresh,
        ActionVariant.Ghost,
        stretch: false);

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
        _versionText.Text = $"Launcher v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}  ·  github.com/Kaofan1/StarTruckerMP";

        Content = BuildLayout();

        var history = _log.Snapshot();
        if (history.Count > 0)
            _logBox.Text = string.Join(Environment.NewLine, history.Skip(Math.Max(0, history.Count - 300)));

        _microphoneCombo.ItemTemplate = new FuncDataTemplate<MicrophoneEntry>((entry, _) =>
            new TextBlock { Text = entry?.Display ?? string.Empty });

        RefreshMicrophoneList();
        TryApplyWindowIcon();
        UpdateServerSummary();

        WireEvents();

        _updateButton.IsVisible = false;
        _updateButton.HorizontalAlignment = HorizontalAlignment.Right;
        _updateButton.Margin = new Thickness(0, 2, 0, 0);
        _updateButton.FontSize = 11.5;
        _updateButton.Foreground = LauncherTheme.AmberBrush;
        _updateButton.Click += (_, _) => _ = UpdateLauncherAsync();
        ToolTip.SetTip(_updateButton, Strings.UpdateHint);

        _languageButton.HorizontalAlignment = HorizontalAlignment.Right;
        _languageButton.Margin = new Thickness(0, 0, 0, 2);
        _languageButton.Flyout = BuildLanguageMenu();
        UpdateLanguageButton();
        _log.Log(Strings.LanguageActive(Strings.Current));

        _log.LineWritten += OnLogLine;

        // The release list is polled while the window is open, so a new build shows up
        // without closing and reopening the launcher.
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(logUpToDate: false);
        _serverTimer.Tick += (_, _) => RefreshLocalServerState();

        Opened += (_, _) =>
        {
            RestoreSavedPosition();
            FitToWorkingArea();
            DetectGame();
            _ = CheckForUpdatesAsync();
            _ = UpdateLocalServerAsync();
            _updateTimer.Start();
            RefreshLocalServerState();
            _serverTimer.Start();
        };
        Closing += (_, _) =>
        {
            _updateTimer.Stop();
            _serverTimer.Stop();
            SaveSettings();
        };
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
            Child = LogoMark.Create(34)
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
                BuildLocalServerCard(),
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
                BuildUpdateCard(),
                BuildConnectCard(),
                BuildModCard(),
                BuildLoaderCard(),
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

    /// <summary>
    /// Hosting is a separate concern from joining: the card only starts or stops the server
    /// on this PC and never touches the client, the game folder or the bookmarks.
    /// </summary>
    private Control BuildLocalServerCard()
    {
        return LauncherTheme.Card(
            Strings.CardLocalServer,
            Strings.LocalServerNote,
            _localServerStatusText,
            _serverPageButton,
            _serverToggleButton,
            LauncherTheme.Caption(Strings.LocalServerCaption));
    }

    private Control BuildLaunchCard()
    {
        var microphoneRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        microphoneRow.Children.Add(_microphoneCombo);

        _refreshMicrophoneButton.Margin = new Thickness(8, 0, 0, 0);
        microphoneRow.Children.Add(_refreshMicrophoneButton);
        Grid.SetColumn(_refreshMicrophoneButton, 1);

        return LauncherTheme.Card(
            Strings.CardLaunch,
            null,
            _steamCheck,
            _sslCheck,
            _closeCheck,
            LauncherTheme.Field(Strings.FieldMicrophone, microphoneRow));
    }

    /// <summary>
    /// Release notes for the pending update. The card stays hidden until a newer build
    /// is found, so the column keeps its normal height when the launcher is current.
    /// </summary>
    private Control BuildUpdateCard()
    {
        var notes = new ScrollViewer
        {
            Content = _updateNotesText,
            MaxHeight = 132,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        actions.Children.Add(_updateNowButton);
        _releasePageButton.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(_releasePageButton);
        Grid.SetColumn(_releasePageButton, 1);

        var card = LauncherTheme.Card(
            Strings.CardUpdate,
            null,
            _updateTitleText,
            notes,
            actions,
            LauncherTheme.Caption(Strings.UpdateNotesCaption));

        card.IsVisible = false;
        _updateCard = card;
        return card;
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
            ActionButtons(_installButton, _repairButton, _uninstallButton));
    }

    /// <summary>
    /// Loader-only card: the client package is published by a separate CI job, so a missing client
    /// must never stop the player from installing the BepInEx that the game needs anyway.
    /// </summary>
    private Control BuildLoaderCard()
    {
        return LauncherTheme.Card(
            Strings.CardLoader,
            Strings.LoaderNote,
            _loaderStatusText,
            ActionButtons(_installLoaderButton, _repairLoaderButton, _uninstallLoaderButton),
            LauncherTheme.Caption(Strings.LoaderCaption));
    }

    /// <summary>
    /// Install and repair share the first row, uninstall spans the full width below them. Three
    /// equal columns made the labels too narrow, and a half-width uninstall looked off-centre.
    /// </summary>
    private static Control ActionButtons(Button install, Button repair, Button uninstall)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 8
        };

        install.Margin = new Thickness(0);
        repair.Margin = new Thickness(8, 0, 0, 0);
        uninstall.Margin = new Thickness(0);

        grid.Children.Add(install);
        grid.Children.Add(repair);
        grid.Children.Add(uninstall);

        Grid.SetColumn(install, 0);
        Grid.SetColumn(repair, 1);
        Grid.SetColumn(uninstall, 0);
        Grid.SetColumnSpan(uninstall, 2);
        Grid.SetRow(uninstall, 1);

        return grid;
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
        _refreshMicrophoneButton.Click += (_, _) => RefreshMicrophoneList();
        _installButton.Click += async (_, _) => await InstallModAsync();
        _repairButton.Click += async (_, _) => await RepairModAsync();
        _uninstallButton.Click += async (_, _) => await UninstallModAsync();
        _installLoaderButton.Click += async (_, _) => await InstallLoaderAsync();
        _repairLoaderButton.Click += async (_, _) => await RepairLoaderAsync();
        _uninstallLoaderButton.Click += async (_, _) => await UninstallLoaderAsync();
        _serverPageButton.Click += (_, _) => OpenServerPage();
        _updateNowButton.Click += async (_, _) => await UpdateLauncherAsync();
        _releasePageButton.Click += (_, _) => OpenUpdatePage();
        _playButton.Click += async (_, _) => await PlayAsync();
        _openGameFolderButton.Click += (_, _) => OpenPath(_install?.Path);
        _openConfigButton.Click += (_, _) => OpenPath(_install is null ? null : PluginConfigWriter.GetConfigPath(_install.Path));
        _addServerButton.Click += (_, _) => AddServer();
        _removeServerButton.Click += (_, _) => RemoveServer();
        _applyServerButton.Click += (_, _) => ApplyServerEdits();
        _serverToggleButton.Click += async (_, _) => await ToggleLocalServerAsync();
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

        // Install only fills what is missing; repairing and updating is the button next to it.
        if (ModInstaller.HasPlugin(_install.Path) && ModInstaller.HasOverlay(_install.Path) && ModInstaller.HasBepInEx(_install.Path))
        {
            _log.Log(Strings.ModAlreadyInstalled);
            return;
        }

        SetBusy(true, Strings.ModInstalling);

        var gamePath = _install.Path;
        var progress = new Progress<string>(message => _log.Log(message));

        try
        {
            // The loader is a separate, optional component: the client installer never downloads
            // BepInEx, and the launcher itself works without either of them.
            await System.Threading.Tasks.Task.Run(async () =>
            {
                await StarTruckerMP.Launcher.Mod.BepInExRuntime.InstallAsync(
                    gamePath,
                    StarTruckerMP.Launcher.Mod.BepInExRuntime.DefaultPayloadRoot,
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

    /// <summary>
    /// Installs only the BepInEx loader. Separate from <see cref="InstallModAsync"/> so a missing
    /// client package, or a player who only wants the loader, never blocks this step.
    /// </summary>
    private async System.Threading.Tasks.Task InstallLoaderAsync()
    {
        if (_install is null || _busy)
            return;

        SetBusy(true, Strings.LoaderInstalling);

        var gamePath = _install.Path;
        var progress = new Progress<string>(message => _log.Log(message));

        try
        {
            await System.Threading.Tasks.Task.Run(async () =>
                await StarTruckerMP.Launcher.Mod.BepInExRuntime.InstallAsync(
                    gamePath,
                    StarTruckerMP.Launcher.Mod.BepInExRuntime.DefaultPayloadRoot,
                    progress,
                    System.Threading.CancellationToken.None));

            _log.Log(Strings.LoaderInstallCompleted);
        }
        catch (Exception exception)
        {
            _log.Log(Strings.LoaderInstallFailed(exception.Message));
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
    /// Reopens the launcher where it was left, but only when that spot is still on a screen -
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

    /// <summary>Rebuilds the microphone picker from the devices Windows currently reports.</summary>
    private void RefreshMicrophoneList()
    {
        var selected = GetSelectedMicrophone();
        var devices = AudioDevices.CaptureDeviceNames();

        var items = new List<MicrophoneEntry> { new(Strings.MicSystemDefault, string.Empty) };
        items.AddRange(devices.Select(name => new MicrophoneEntry(name, name)));

        // A device that is unplugged right now stays visible, so the saved choice is not lost.
        if (selected.Length > 0 && devices.All(name => !string.Equals(name, selected, StringComparison.OrdinalIgnoreCase)))
            items.Add(new MicrophoneEntry(selected, selected));

        _microphoneCombo.ItemsSource = null;
        _microphoneCombo.ItemsSource = items;
        _microphoneCombo.SelectedItem = items.FirstOrDefault(entry =>
            string.Equals(entry.Value, selected, StringComparison.OrdinalIgnoreCase)) ?? items[0];
    }

    private string GetSelectedMicrophone() =>
        (_microphoneCombo.SelectedItem as MicrophoneEntry)?.Value ?? _settings.MicrophoneDeviceName ?? string.Empty;

    /// <summary>Renders the header mark into a window icon so the taskbar is not blank either.</summary>
    private void TryApplyWindowIcon()
    {
        try
        {
            const int size = 64;
            var logo = LogoMark.Create(size);
            logo.Measure(new Size(size, size));
            logo.Arrange(new Rect(0, 0, size, size));

            var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            bitmap.Render(logo);
            Icon = new WindowIcon(bitmap);
        }
        catch
        {
            // The header mark is the important part; the taskbar icon is a bonus.
        }
    }

    private sealed record MicrophoneEntry(string Display, string Value)
    {
        public override string ToString() => Display;
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

    private async System.Threading.Tasks.Task CheckForUpdatesAsync(bool logUpToDate = true)
    {
        // Installing the mod or updating the launcher keeps the network and the log to itself.
        if (_busy)
            return;

        try
        {
            var update = await UpdateChecker.FindUpdateAsync(System.Threading.CancellationToken.None);

            if (update is null)
            {
                _pendingUpdate = null;
                _updateButton.IsVisible = false;

                if (_updateCard is not null)
                    _updateCard.IsVisible = false;

                if (logUpToDate)
                    _log.Log(Strings.UpToDate(UpdateChecker.CurrentVersion.ToString(3)));

                return;
            }

            _pendingUpdate = update;
            _updateButton.Tag = update.PageUrl;
            _updateButton.Content = Strings.UpdateAvailable(update.Name);
            _updateButton.IsVisible = true;
            _updateButton.IsEnabled = !_busy;

            _updateTitleText.Text = update.Name;
            _updateNotesText.Text = FormatNotes(update.Notes);

            if (_updateCard is not null)
                _updateCard.IsVisible = true;

            if (logUpToDate)
                _log.Log(Strings.UpdateFound(update.Name, update.Tag, update.PageUrl));
        }
        catch (Exception exception)
        {
            // A failed update check must never get in the way of launching the game.
            if (logUpToDate)
                _log.Log(Strings.UpdateCheckSkipped(exception.Message));
        }
    }

    /// <summary>
    /// GitHub writes the release notes as Markdown with author and commit links. The card
    /// has one narrow column, so headings, bullets and links are reduced to plain lines.
    /// </summary>
    private static string FormatNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return Strings.UpdateNoNotes;

        var lines = new List<string>();

        foreach (var raw in notes.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
                line = line.TrimStart('#').Trim();
            else if (line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal))
                line = "• " + line[2..].Trim();

            line = line.Replace("**", string.Empty).Replace("`", string.Empty);

            // Generated notes end each entry with "by @author in <commit url>" - useless here.
            line = Regex.Replace(line, @"\s+by\s+@\S+\s+in\s+https?://\S+$", string.Empty).Trim();

            // GitHub's own "Full Changelog" line points at this repository, so only the tags stay.
            line = Regex.Replace(line, @"^Full Changelog:\s*", Strings.UpdateCompareLabel + " ", RegexOptions.IgnoreCase);
            line = line.Replace("https://github.com/Kaofan1/StarTruckerMP/compare/", string.Empty)
                .Replace("https://github.com/Kaofan1/StarTruckerMP/", string.Empty)
                .Replace("https://github.com/", string.Empty)
                .Trim();

            if (line.Length > 0)
                lines.Add(line);
        }

        var text = string.Join(Environment.NewLine, lines);
        if (text.Length == 0)
            return Strings.UpdateNoNotes;

        return text.Length > 1200 ? text[..1200].TrimEnd() + "…" : text;
    }

    /// <summary>
    /// Clicks on the update banner download the release and let a helper replace the
    /// launcher files after this window closes, so the new build starts on its own.
    /// </summary>
    private async System.Threading.Tasks.Task UpdateLauncherAsync()
    {
        if (_pendingUpdate is null)
        {
            OpenUpdatePage();
            return;
        }

        if (_busy)
            return;

        var update = _pendingUpdate;
        SetBusy(true, Strings.UpdateDownloading(update.Name));
        _updateButton.IsEnabled = false;

        try
        {
            await LauncherUpdater.StageAsync(update, new Progress<string>(_log.Log), System.Threading.CancellationToken.None);

            _log.Log(Strings.UpdateReady(update.Name));
            SetBusy(false, Strings.UpdateReady(update.Name));

            // Give the helper a moment to arm itself before the files get unlocked.
            await System.Threading.Tasks.Task.Delay(900);
            Close();
        }
        catch (Exception exception)
        {
            _log.Log(Strings.UpdateFailed(exception.Message));
            SetBusy(false, string.Empty);
            _updateButton.IsEnabled = true;
        }
    }

    /// <summary>Reads the live server state; a timer keeps it current even if the server was started outside the launcher.</summary>
    private void RefreshLocalServerState()
    {
        if (_busy)
            return;

        var status = LocalServer.Detect();
        _localServer = status;
        _serverPageButton.IsVisible = status.State == LocalServerState.Running;

        switch (status.State)
        {
            case LocalServerState.Running:
                _localServerStatusText.Text = Strings.LocalServerRunning(status.ProcessId);
                _localServerStatusText.Foreground = LauncherTheme.TealBrush;
                SetButtonText(_serverToggleButton, Strings.StopLocalServer);
                break;

            case LocalServerState.Stopped:
                _localServerStatusText.Text = Strings.LocalServerStopped;
                _localServerStatusText.Foreground = LauncherTheme.AmberBrush;
                SetButtonText(_serverToggleButton, Strings.StartLocalServer);
                break;

            default:
                _localServerStatusText.Text = Strings.LocalServerMissing;
                _localServerStatusText.Foreground = LauncherTheme.MutedBrush;
                SetButtonText(_serverToggleButton, Strings.StartLocalServer);
                break;
        }
    }

    /// <summary>
    /// Pulls a newer local server package on start, so the card never runs last release's build.
    /// A running server is left alone: Windows keeps its files locked until it stops.
    /// </summary>
    private async System.Threading.Tasks.Task UpdateLocalServerAsync()
    {
        if (_busy)
            return;

        try
        {
            var tag = await LocalServer.UpdateIfOutdatedAsync(
                new Progress<string>(_log.Log),
                System.Threading.CancellationToken.None);

            if (string.IsNullOrEmpty(tag))
                return;

            // The timer may fire while the package is unpacking, so refresh once more.
            _log.Log(Strings.LocalServerUpdated(tag));
            RefreshLocalServerState();
        }
        catch (Exception exception)
        {
            // An update problem must never block the launcher; the start path retries it.
            _log.Log(Strings.LocalServerUpdateFailed(exception.Message));
        }
    }

    private async System.Threading.Tasks.Task ToggleLocalServerAsync()
    {
        if (_busy)
            return;

        var status = _localServer ?? LocalServer.Detect();

        if (status.State == LocalServerState.Running)
        {
            SetBusy(true, Strings.StopLocalServer);

            try
            {
                if (LocalServer.Stop())
                    _log.Log(Strings.LocalServerStoppedLog);
                else
                    _log.Log(Strings.LocalServerStopFailed);
            }
            catch (Exception exception)
            {
                _log.Log(Strings.LocalServerStopFailed + " " + exception.Message);
            }
            finally
            {
                SetBusy(false, string.Empty);
                RefreshLocalServerState();
            }

            return;
        }

        SetBusy(true, Strings.LocalServerLookingUp);

        try
        {
            var started = await LocalServer.StartAsync(new Progress<string>(_log.Log), System.Threading.CancellationToken.None);
            _log.Log(Strings.LocalServerStarted(started.ExecutablePath ?? string.Empty));
            SetBusy(false, Strings.LocalServerRunning(started.ProcessId));
        }
        catch (Exception exception)
        {
            _log.Log(Strings.LocalServerStartFailed(exception.Message));
            SetBusy(false, string.Empty);
        }

        RefreshLocalServerState();
    }

    /// <summary>
    /// Repair keeps install and update apart: the plugin folder is deleted first, so a half-copied
    /// or hand-edited install cannot survive the download.
    /// </summary>
    private async System.Threading.Tasks.Task RepairModAsync()
    {
        if (_install is null || _busy)
            return;

        var gamePath = _install.Path;

        if (ModInstaller.IsGameRunning())
        {
            _log.Log(Strings.GameRunningBlock);
            return;
        }

        SetBusy(true, Strings.ModRepairing);

        var progress = new Progress<string>(message => _log.Log(message));

        try
        {
            await System.Threading.Tasks.Task.Run(async () =>
            {
                await StarTruckerMP.Launcher.Mod.BepInExRuntime.InstallAsync(
                    gamePath,
                    StarTruckerMP.Launcher.Mod.BepInExRuntime.DefaultPayloadRoot,
                    progress,
                    System.Threading.CancellationToken.None);

                ModInstaller.Uninstall(gamePath);
                await _installer.InstallAsync(gamePath, progress, System.Threading.CancellationToken.None);
            });

            _log.Log(Strings.ModRepairCompleted);
        }
        catch (Exception exception)
        {
            _log.Log(Strings.ModRepairFailed(exception.Message));
        }
        finally
        {
            SetBusy(false, string.Empty);
            RefreshModState();
        }
    }

    private async System.Threading.Tasks.Task UninstallModAsync()
    {
        if (_install is null || _busy)
            return;

        var gamePath = _install.Path;

        if (!ModInstaller.IsModInstalled(gamePath))
        {
            _log.Log(Strings.ModUninstallSkipped);
            return;
        }

        if (ModInstaller.IsGameRunning())
        {
            _log.Log(Strings.GameRunningBlock);
            return;
        }

        SetBusy(true, Strings.ModUninstalling);

        try
        {
            var removed = await System.Threading.Tasks.Task.Run(() => ModInstaller.Uninstall(gamePath));
            _log.Log(removed ? Strings.ModUninstallCompleted : Strings.ModUninstallSkipped);
        }
        catch (Exception exception)
        {
            _log.Log(Strings.ModUninstallFailed(exception.Message));
        }
        finally
        {
            SetBusy(false, string.Empty);
            RefreshModState();
        }
    }

    private async System.Threading.Tasks.Task RepairLoaderAsync()
    {
        if (_install is null || _busy)
            return;

        var gamePath = _install.Path;

        if (ModInstaller.IsGameRunning())
        {
            _log.Log(Strings.GameRunningBlock);
            return;
        }

        SetBusy(true, Strings.LoaderRepairing);

        var progress = new Progress<string>(message => _log.Log(message));

        try
        {
            await System.Threading.Tasks.Task.Run(async () =>
            {
                StarTruckerMP.Launcher.Mod.BepInExRuntime.Uninstall(gamePath);

                await StarTruckerMP.Launcher.Mod.BepInExRuntime.InstallAsync(
                    gamePath,
                    StarTruckerMP.Launcher.Mod.BepInExRuntime.DefaultPayloadRoot,
                    progress,
                    System.Threading.CancellationToken.None);
            });

            _log.Log(Strings.LoaderRepairCompleted);
        }
        catch (Exception exception)
        {
            _log.Log(Strings.LoaderRepairFailed(exception.Message));
        }
        finally
        {
            SetBusy(false, string.Empty);
            RefreshModState();
        }
    }

    private async System.Threading.Tasks.Task UninstallLoaderAsync()
    {
        if (_install is null || _busy)
            return;

        var gamePath = _install.Path;

        if (!ModInstaller.HasBepInEx(gamePath) && !ModInstaller.HasMonoBepInEx(gamePath))
        {
            _log.Log(Strings.LoaderNotInstalled);
            return;
        }

        if (ModInstaller.IsGameRunning())
        {
            _log.Log(Strings.GameRunningBlock);
            return;
        }

        SetBusy(true, Strings.LoaderUninstalling);

        try
        {
            var removed = await System.Threading.Tasks.Task.Run(
                () => StarTruckerMP.Launcher.Mod.BepInExRuntime.Uninstall(gamePath));

            if (removed)
            {
                _log.Log(Strings.LoaderUninstallCompleted);
                _log.Log(Strings.LoaderUninstallKeptPlugins);
            }
            else
            {
                _log.Log(Strings.LoaderNotInstalled);
            }
        }
        catch (Exception exception)
        {
            _log.Log(Strings.LoaderUninstallFailed(exception.Message));
        }
        finally
        {
            SetBusy(false, string.Empty);
            RefreshModState();
        }
    }

    /// <summary>Opens the local server status page in the default browser.</summary>
    private void OpenServerPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LocalServer.StatusPageUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _log.Log(Strings.ReleasePageFailed(exception.Message));
        }
    }

    /// <summary>Swaps the label of a themed button without losing its border and palette.</summary>
    private static void SetButtonText(Button button, string text)
    {
        if (button.Content is Border { Child: TextBlock block })
            block.Text = text;
        else
            button.Content = text;
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
                GetSelectedMicrophone());

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
        _updateButton.IsEnabled = !_busy && _pendingUpdate is not null;

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

        if (_install is null)
        {
            _loaderStatusText.Text = Strings.ModUnknown;
            _loaderStatusText.Foreground = LauncherTheme.MutedBrush;
        }
        else if (ModInstaller.HasBepInEx(_install.Path))
        {
            _loaderStatusText.Text = Strings.LoaderInstalled;
            _loaderStatusText.Foreground = LauncherTheme.TealBrush;
        }
        else if (ModInstaller.HasMonoBepInEx(_install.Path))
        {
            _loaderStatusText.Text = Strings.LoaderMono;
            _loaderStatusText.Foreground = LauncherTheme.DangerBrush;
        }
        else
        {
            _loaderStatusText.Text = Strings.LoaderMissing;
            _loaderStatusText.Foreground = LauncherTheme.AmberBrush;
        }

        _installButton.IsEnabled = _install is not null && !_busy;
        _repairButton.IsEnabled = _install is not null && !_busy;
        _uninstallButton.IsEnabled = _install is not null && !_busy;
        _installLoaderButton.IsEnabled = _install is not null && !_busy;
        _repairLoaderButton.IsEnabled = _install is not null && !_busy;
        _uninstallLoaderButton.IsEnabled = _install is not null && !_busy;
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
        _settings.MicrophoneDeviceName = GetSelectedMicrophone();
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
        _repairButton.IsEnabled = !busy && _install is not null;
        _uninstallButton.IsEnabled = !busy && _install is not null;
        _installLoaderButton.IsEnabled = !busy && _install is not null;
        _repairLoaderButton.IsEnabled = !busy && _install is not null;
        _uninstallLoaderButton.IsEnabled = !busy && _install is not null;
        _playButton.IsEnabled = !busy && _install is not null;
        _detectButton.IsEnabled = !busy;
        _browseButton.IsEnabled = !busy;
        _updateButton.IsEnabled = !busy && _pendingUpdate is not null;
        _serverToggleButton.IsEnabled = !busy;
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
