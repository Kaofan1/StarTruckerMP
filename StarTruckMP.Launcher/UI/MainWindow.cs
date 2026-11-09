using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StarTruckMP.Launcher.Models;
using StarTruckMP.Launcher.Services;

namespace StarTruckMP.Launcher.UI;

internal sealed class MainWindow : Window
{
    private static readonly IBrush CardBackground = new SolidColorBrush(Color.Parse("#1B2030"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#7FB4FF"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#A7B0C0"));

    private readonly LauncherLogger _log = new();
    private readonly SettingsStore _store = new();
    private readonly ModInstaller _installer;
    private readonly LauncherSettings _settings;

    private GameInstall? _install;
    private bool _busy;
    private bool _refreshingServerList;

    private readonly TextBlock _gamePathText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _modStatusText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush };
    private readonly TextBlock _versionText = new() { Foreground = MutedBrush, FontSize = 12 };

    private readonly TextBox _logBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
        FontSize = 12,
        Height = 160
    };

    private readonly Button _detectButton = MakeButton("Detect game");
    private readonly Button _browseButton = MakeButton("Choose folder…");
    private readonly Button _installButton = MakeButton("Install / repair mod");
    private readonly Button _openGameFolderButton = MakeButton("Open game folder");
    private readonly Button _openConfigButton = MakeButton("Open config");
    private readonly Button _playButton = new()
    {
        Content = "PLAY",
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        Padding = new Thickness(28, 14),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private readonly ComboBox _serverCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 260 };
    private readonly Button _addServerButton = MakeButton("Add");
    private readonly Button _removeServerButton = MakeButton("Remove");
    private readonly Button _applyServerButton = MakeButton("Save server");
    private readonly TextBox _nameBox = new() { Watermark = "Server name" };
    private readonly TextBox _addressBox = new() { Watermark = "host or IP" };
    private readonly TextBox _portBox = new() { Watermark = "port", Width = 90 };
    private readonly TextBox _microphoneBox = new() { Watermark = "leave empty for the system default" };

    private readonly CheckBox _steamCheck = new() { Content = "Launch through Steam (recommended - keeps Steam login working)" };
    private readonly CheckBox _sslCheck = new() { Content = "Ignore SSL certificate errors (needed for self-signed servers)" };
    private readonly CheckBox _closeCheck = new() { Content = "Close the launcher after the game starts" };

    public MainWindow()
    {
        _settings = _store.Load();
        _installer = new ModInstaller(_log);

        Title = "Star Trucker Multiplayer";
        Width = 900;
        Height = 780;
        MinWidth = 720;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _steamCheck.IsChecked = _settings.LaunchThroughSteam;
        _sslCheck.IsChecked = _settings.IgnoreSslValidation;
        _closeCheck.IsChecked = _settings.CloseLauncherAfterLaunch;
        _microphoneBox.Text = _settings.MicrophoneDeviceName;
        _versionText.Text = $"Launcher v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}  ·  github.com/Kaofan1/StarTruckerMP";

        Content = BuildLayout();
        WireEvents();

        _log.LineWritten += OnLogLine;
        Opened += (_, _) => DetectGame();
        Closing += (_, _) => SaveSettings();
    }

    private Control BuildLayout()
    {
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "STAR TRUCKER MULTIPLAYER", FontSize = 24, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Launcher, mod installer and server bookmarks", Foreground = MutedBrush },
                _versionText
            }
        };

        var gameSection = Card("GAME INSTALLATION",
            _gamePathText,
            Row(_detectButton, _browseButton, _openGameFolderButton, _openConfigButton),
            new TextBlock { Text = "MOD", FontWeight = FontWeight.SemiBold, Foreground = MutedBrush, Margin = new Thickness(0, 6, 0, 0) },
            _modStatusText,
            Row(_installButton, _statusText));

        var serverSection = Card("SERVER",
            Row(_serverCombo, _addServerButton, _removeServerButton),
            Row(new TextBlock { Text = "Name", Width = 60, VerticalAlignment = VerticalAlignment.Center }, _nameBox),
            Row(new TextBlock { Text = "Host", Width = 60, VerticalAlignment = VerticalAlignment.Center }, _addressBox),
            Row(new TextBlock { Text = "Port", Width = 60, VerticalAlignment = VerticalAlignment.Center }, _portBox),
            Row(_applyServerButton));

        var launchSection = Card("LAUNCH",
            _steamCheck,
            _sslCheck,
            _closeCheck,
            Row(new TextBlock { Text = "Microphone", Width = 90, VerticalAlignment = VerticalAlignment.Center }, _microphoneBox),
            _playButton);

        var logSection = Card("LOG", _logBox,
            Row(new TextBlock { Text = LauncherLogger.LogPath, Foreground = MutedBrush, FontSize = 11 }));

        var stack = new StackPanel
        {
            Spacing = 12,
            Children = { header, gameSection, serverSection, launchSection, logSection }
        };

        return new ScrollViewer { Content = stack, Padding = new Thickness(20) };
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
            _gamePathText.Text = "Star Trucker was not found. Use “Choose folder…” and pick the folder that contains Star Trucker.exe.";
            _gamePathText.Foreground = Brushes.Orange;
            _log.Log("Star Trucker was not detected.");
        }
        else
        {
            _settings.GamePath = _install.Path;
            _gamePathText.Text = _install.Path;
            _gamePathText.Foreground = Brushes.White;
            _log.Log($"Using Star Trucker at {_install.Path}");

            if (_install.Warning is not null)
                _log.Log("Warning: " + _install.Warning);
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
            _log.Log("That folder does not contain Star Trucker.exe.");
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

        SetBusy(true, "Installing…");

        var gamePath = _install.Path;
        var progress = new Progress<string>(message => _log.Log(message));

        try
        {
            await System.Threading.Tasks.Task.Run(() => _installer.InstallAsync(gamePath, progress, System.Threading.CancellationToken.None));
            _log.Log("Mod installation completed.");
        }
        catch (Exception exception)
        {
            _log.Log("Mod installation failed: " + exception.Message);
        }
        finally
        {
            SetBusy(false, string.Empty);
            RefreshModState();
        }
    }

    private async System.Threading.Tasks.Task PlayAsync()
    {
        if (_install is null || _busy)
            return;

        var server = GetSelectedServer();
        if (server is null)
        {
            _log.Log("Add a server before launching.");
            return;
        }

        if (string.IsNullOrWhiteSpace(server.Address))
        {
            _log.Log("The selected server has no address.");
            return;
        }

        if (!int.TryParse(server.Port, out var port) || port is < 1 or > 65535)
        {
            _log.Log($"'{server.Port}' is not a valid port.");
            return;
        }

        if (!ModInstaller.HasPlugin(_install.Path))
        {
            _log.Log("The StarTruckMP client is not installed yet — press “Install / repair mod” first.");
            return;
        }

        try
        {
            PluginConfigWriter.Apply(
                _install.Path,
                server.Address.Trim(),
                port.ToString(),
                _sslCheck.IsChecked == true,
                _microphoneBox.Text?.Trim() ?? string.Empty);

            _log.Log($"Server set to {server.Address.Trim()}:{port}.");

            var throughSteam = _steamCheck.IsChecked != false;
            GameLauncher.Launch(_install, throughSteam);
            _log.Log(throughSteam
                ? "Asked Steam to start Star Trucker. The overlay appears once you load into the sector."
                : "Started Star Trucker directly. Steam login may not work this way.");

            if (_closeCheck.IsChecked == true)
            {
                await System.Threading.Tasks.Task.Delay(1500);
                Close();
            }
        }
        catch (Exception exception)
        {
            _log.Log("Launch failed: " + exception.Message);
        }
    }

    private void RefreshModState()
    {
        if (_install is null)
        {
            _modStatusText.Text = "Unknown — the game folder is not set.";
            _modStatusText.Foreground = MutedBrush;
        }
        else
        {
            var hasBepInEx = ModInstaller.HasBepInEx(_install.Path);
            var hasPlugin = ModInstaller.HasPlugin(_install.Path);
            var hasOverlay = ModInstaller.HasOverlay(_install.Path);

            if (hasBepInEx && hasPlugin && hasOverlay)
            {
                _modStatusText.Text = "Installed (BepInEx + client + overlay). Ready to play.";
                _modStatusText.Foreground = Brushes.LightGreen;
            }
            else if (!hasBepInEx && !hasPlugin)
            {
                _modStatusText.Text = "Not installed.";
                _modStatusText.Foreground = Brushes.Orange;
            }
            else
            {
                var missing = new List<string>();
                if (!hasBepInEx) missing.Add("BepInEx");
                if (!hasPlugin) missing.Add("client");
                if (!hasOverlay) missing.Add("overlay");
                _modStatusText.Text = $"Partial install — missing: {string.Join(", ", missing)}. Press “Install / repair mod”.";
                _modStatusText.Foreground = Brushes.Orange;
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
    }

    private void AddServer()
    {
        var server = new ServerEntry { Name = "New server", Address = "127.0.0.1", Port = "7777" };
        _settings.Servers.Add(server);
        _settings.SelectedServerId = server.Id;

        _refreshingServerList = true;
        _serverCombo.ItemsSource = null;
        _serverCombo.ItemsSource = _settings.Servers;
        _serverCombo.SelectedItem = server;
        _refreshingServerList = false;

        LoadSelectedServerIntoEditor();
        _store.Save(_settings);
        _log.Log("Added a new server entry.");
    }

    private void RemoveServer()
    {
        var server = GetSelectedServer();
        if (server is null)
            return;

        if (_settings.Servers.Count <= 1)
        {
            _log.Log("At least one server entry has to stay.");
            return;
        }

        _settings.Servers.Remove(server);
        _settings.SelectedServerId = _settings.Servers[0].Id;
        RefreshServerList();
        _store.Save(_settings);
        _log.Log($"Removed server “{server.Name}”.");
    }

    private void ApplyServerEdits()
    {
        var server = GetSelectedServer();
        if (server is null)
            return;

        var address = _addressBox.Text?.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            _log.Log("The server address cannot be empty.");
            return;
        }

        if (!int.TryParse(_portBox.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            _log.Log("The port has to be a number between 1 and 65535.");
            return;
        }

        server.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? address : _nameBox.Text!.Trim();
        server.Address = address;
        server.Port = port.ToString();

        RefreshServerList();
        _store.Save(_settings);
        _log.Log($"Saved server “{server.Name}” ({server.Address}:{server.Port}).");
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
        _store.Save(_settings);
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _statusText.Text = status;
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
            _log.Log("Nothing to open yet.");
            return;
        }

        try
        {
            GameLauncher.OpenFolder(path);
        }
        catch (Exception exception)
        {
            _log.Log($"Could not open {path}: {exception.Message}");
        }
    }

    private static Button MakeButton(string text) => new() { Content = text, Padding = new Thickness(12, 6) };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static Border Card(string title, params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Foreground = AccentBrush
        });

        foreach (var child in children)
            panel.Children.Add(child);

        return new Border
        {
            Background = CardBackground,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = panel
        };
    }
}
