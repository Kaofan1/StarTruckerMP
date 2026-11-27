#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StarTruckerMP.Client.Synchronization;

namespace StarTruckerMP.Client.UI;

/// <summary>
/// Serves the "Multiplayer" panel shown from the game's main menu. The page is fully self
/// contained: it asks the plugin for the directory list and the plugin fetches it, because the
/// overlay browser may refuse the self-signed certificate a game server uses.
/// </summary>
internal static class MultiplayerPanel
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowTrailingCommas = true };

    private static readonly LocalPageServer Page = new("StarTruckerMP.MultiplayerPanel");

    /// <summary>Opens the panel in the overlay, above the running game.</summary>
    public static void Open()
    {
        try
        {
            var url = Page.Publish(BuildHtml());
            HudPanel.SuspendForPanel();
            // One pipe write: the page is loaded first and the window only appears when it is
            // ready, then it takes the keyboard. Sending these separately let the old page show.
            OverlayManager.Panel.ShowPage(url, interactive: true);
            App.Log.LogInfo("[Multiplayer] Panel opened.");
        }
        catch (Exception exception)
        {
            // A missing overlay process must never break the game menu.
            App.Log.LogWarning("[Multiplayer] Could not open the panel: " + exception.Message);
            HudPanel.ResumeAfterPanel();
        }
    }

    public static void Close()
    {
        try
        {
            OverlayManager.Panel.SetInteractiveMode(false);
            OverlayManager.Panel.Hide();
            HudPanel.ResumeAfterPanel();
        }
        catch (Exception exception)
        {
            App.Log.LogWarning("[Multiplayer] Could not close the panel: " + exception.Message);
        }
    }

    /// <summary>Messages sent by the panel through the overlay pipe.</summary>
    public static void HandleMessage(string type, string? payload)
    {
        try
        {
            switch (type)
            {
                case "multiplayerRefresh":
                    Refresh(payload);
                    break;

                case "joinServer":
                    Join(payload);
                    break;

                case "closeMultiplayer":
                    Close();
                    break;
            }
        }
        catch (Exception exception)
        {
            App.Log.LogWarning($"[Multiplayer] {type} failed: {exception.Message}");
        }
    }

    private static void Refresh(string? payload)
    {
        // The address field is an optional override: left empty it reads the configured
        // directory, so the list appears without typing anything.
        var typed = ReadTypedUrl(payload);
        var url = string.IsNullOrWhiteSpace(typed) ? ConfiguredDirectoryUrl() : typed;

        if (!string.IsNullOrWhiteSpace(typed))
        {
            App.DirectoryUrl.Value = typed;
            SaveConfig();
        }

        // Fetching runs off the game thread; the result goes back through the overlay pipe.
        Plugin.StartAttachedThread(() =>
        {
            var (error, servers) = FetchAsync(url).GetAwaiter().GetResult();
            OverlayManager.Panel.PostMessage("multiplayerServers", new { url, error, servers });
        });
    }

    private static string? ReadTypedUrl(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload, ParseOptions);
            if (document.RootElement.TryGetProperty("url", out var value) &&
                value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim();
        }
        catch (JsonException)
        {
            // Fall back to the configured address.
        }

        return null;
    }

    /// <summary>The directory the panel reads when its own address field is left empty.</summary>
    private static string ConfiguredDirectoryUrl()
    {
        var configured = App.DirectoryUrl.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return $"https://{App.ServerAddress.Value}:{App.ServerPort.Value}";
    }

    private static void Join(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        string? host = null;
        var port = 0;
        string? name = null;

        using (var document = JsonDocument.Parse(payload, ParseOptions))
        {
            var root = document.RootElement;
            if (root.TryGetProperty("host", out var hostValue) && hostValue.ValueKind == JsonValueKind.String)
                host = hostValue.GetString()?.Trim();
            if (root.TryGetProperty("port", out var portValue) && portValue.TryGetInt32(out var parsedPort))
                port = parsedPort;
            if (root.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String)
                name = nameValue.GetString();
        }

        host = NormalizeHost(host, ref port);
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            OverlayManager.Panel.PostMessage("serverSelected", new { error = "Invalid server address." });
            return;
        }

        // Picking a server is the only thing that arms the connection: without it a loaded save
        // stays a single-player session even though the address is remembered below.
        Network.RequestConnect();

        App.ServerAddress.Value = host;
        App.ServerPort.Value = port.ToString();
        SaveConfig();

        App.Log.LogInfo($"[Multiplayer] Selected server {host}:{port} ({name ?? host}). Load a save to connect.");
        OverlayManager.Panel.PostMessage("serverSelected", new { host, port, name = name ?? host });
    }

    /// <summary>
    /// A typed address may arrive as <c>https://host/</c>, <c>host:port</c> or a bare IP. The port
    /// is optional, so an address without one falls back to the configured server port.
    /// </summary>
    private static string? NormalizeHost(string? typed, ref int port)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        var value = typed.Trim();

        if (value.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.Host;

        // Bracketed IPv6 keeps its colons; everything else may carry a trailing :port.
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            value = value[1..^1];
        }
        else if (value.Contains(':', StringComparison.Ordinal))
        {
            var separator = value.LastIndexOf(':');
            if (separator > 0 && int.TryParse(value[(separator + 1)..], out var inlinePort))
            {
                value = value[..separator];
                if (port <= 0)
                    port = inlinePort;
            }
        }

        if (port <= 0)
            port = DefaultPort();

        return value.Trim();
    }

    private static int DefaultPort() =>
        int.TryParse(App.ServerPort.Value, out var configured) && configured is > 0 and <= 65535
            ? configured
            : 7777;

    private static async Task<(string? Error, object Servers)> FetchAsync(string url)
    {
        var baseUrl = NormalizeBaseUrl(url);
        if (baseUrl is null)
            return ("Enter the directory host and port first.", Array.Empty<object>());

        string? lastError = null;

        foreach (var candidate in Candidates(baseUrl + "/directory/servers"))
        {
            try
            {
                using var response = await Client.GetAsync(candidate).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)response.StatusCode}";
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var rows = ParseServers(json);
                return (null, Describe(rows, ProbeAll(rows)));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastError = exception.Message;
            }
        }

        return (lastError ?? "The directory did not answer.", Array.Empty<object>());
    }

    /// <summary>One directory entry as the panel needs it.</summary>
    private sealed record ServerRow(
        string Name,
        string Host,
        int Port,
        int Players,
        int Capacity,
        string? Description,
        string? Version);

    private static List<ServerRow> ParseServers(string json)
    {
        var servers = new List<ServerRow>();

        using var document = JsonDocument.Parse(json, ParseOptions);
        if (!document.RootElement.TryGetProperty("servers", out var list) ||
            list.ValueKind != JsonValueKind.Array)
            return servers;

        foreach (var item in list.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var host = ReadString(item, "host");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
                continue;

            servers.Add(new ServerRow(
                name,
                host,
                ReadInt(item, "port"),
                ReadInt(item, "players"),
                ReadInt(item, "capacity"),
                ReadString(item, "description"),
                ReadString(item, "version")));
        }

        return servers;
    }

    /// <summary>Pairs every row with its measured ping, -1 when the probe failed.</summary>
    private static object[] Describe(IReadOnlyList<ServerRow> rows, IReadOnlyList<int> pings)
    {
        var servers = new object[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            servers[index] = new
            {
                row.Name,
                row.Host,
                row.Port,
                row.Players,
                row.Capacity,
                row.Description,
                row.Version,
                ping = pings[index]
            };
        }

        return servers;
    }

    private const int PingTimeoutMs = 1500;
    private const int PingBatchSize = 16;

    /// <summary>
    /// The directory only knows that a server announced itself, so the panel measures the round
    /// trip of a TCP connect to the server port itself instead of trusting a stale number. The
    /// probes run on their own threads, because this method already runs off the game thread.
    /// </summary>
    private static int[] ProbeAll(IReadOnlyList<ServerRow> rows)
    {
        var pings = new int[rows.Count];
        Array.Fill(pings, -1);

        for (var start = 0; start < rows.Count; start += PingBatchSize)
        {
            var end = Math.Min(start + PingBatchSize, rows.Count);
            var threads = new List<Thread>(end - start);

            for (var index = start; index < end; index++)
            {
                var slot = index;
                var row = rows[slot];
                var thread = new Thread(() => pings[slot] = Probe(row.Host, row.Port))
                {
                    IsBackground = true,
                    Name = "StarTruckerMP.Ping"
                };

                threads.Add(thread);
                thread.Start();
            }

            foreach (var thread in threads)
                thread.Join(PingTimeoutMs + 250);
        }

        return pings;
    }

    private static int Probe(string host, int port)
    {
        if (port is < 1 or > 65535)
            return -1;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host.Trim('[', ']'), port);
            if (!connect.Wait(PingTimeoutMs))
                return -1;

            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch (Exception exception) when (exception is SocketException or AggregateException or ArgumentException or ObjectDisposedException)
        {
            return -1;
        }
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string? NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim().TrimEnd('/');
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out _) ? trimmed : null;
    }

    /// <summary>Self-signed game servers answer https; plain http is the fallback.</summary>
    private static System.Collections.Generic.IEnumerable<string> Candidates(string url)
    {
        yield return url;

        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            yield return "http://" + url["https://".Length..];
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private static void SaveConfig()
    {
        try
        {
            App.ConfigFile?.Save();
        }
        catch (Exception exception)
        {
            App.Log.LogWarning("[Multiplayer] Could not save the config: " + exception.Message);
        }
    }

    private static string BuildHtml() => PanelHtml
        .Replace("__DIRECTORY_URL__", JsonSerializer.Serialize(App.DirectoryUrl.Value), StringComparison.Ordinal)
        .Replace("__DEFAULT_PORT__", JsonSerializer.Serialize(DefaultPort()), StringComparison.Ordinal);

    /// <summary>
    /// Plain HTML on purpose: the Svelte overlay page needs a running game server, and this
    /// panel has to work in the main menu, before any server is chosen.
    /// </summary>
    private const string PanelHtml = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>StarTruckerMP</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body {
    margin: 0; height: 100vh; display: flex; align-items: center; justify-content: center;
    background: radial-gradient(circle at 20% 0%, #14202e 0%, #070c14 60%);
    font-family: "Segoe UI", system-ui, sans-serif; color: #f3e7d0;
  }
  .panel {
    width: min(760px, 92vw); height: min(380px, 86vh); display: flex; flex-direction: column;
    background: #101823; border: 1px solid #24303f; border-top: 3px solid #f2a33c;
    border-radius: 6px; box-shadow: 0 18px 60px rgb(0 0 0 / 55%);
  }
  /* The panel keeps one height, so the tabs never move when the tab changes. */
  .panel > section { flex: 1; min-height: 0; display: flex; flex-direction: column; }
  header { display: flex; align-items: center; gap: 12px; padding: 14px 18px; border-bottom: 1px solid #24303f; }
  h1 { margin: 0; font-size: 15px; letter-spacing: .16em; text-transform: uppercase; color: #f2a33c; flex: 1; }
  .close { background: transparent; color: #93a1af; border: 0; font-size: 20px; cursor: pointer; line-height: 1; }
  .close:hover { color: #f3e7d0; }
  nav.tabs { display: flex; gap: 6px; padding: 12px 18px 0; }
  .tab {
    background: transparent; color: #93a1af; border: 1px solid #24303f; border-radius: 4px;
    padding: 6px 14px; font-size: 12px; cursor: pointer;
  }
  .tab:hover { color: #f3e7d0; }
  .tab.active { background: #f2a33c; border-color: #f2a33c; color: #20140a; font-weight: 700; }
  .toolbar { display: flex; gap: 8px; padding: 12px 18px 8px; }
  input {
    flex: 1; min-width: 0; background: #0b121b; color: #f3e7d0; border: 1px solid #24303f;
    border-radius: 4px; padding: 8px 10px; font-size: 13px;
  }
  input:focus { outline: none; border-color: #f2a33c; }
  input.port { flex: 0 0 108px; text-align: center; }
  button.action {
    background: #f2a33c; color: #20140a; border: 0; border-radius: 4px;
    padding: 8px 16px; font-weight: 700; cursor: pointer; font-size: 13px; white-space: nowrap;
  }
  button.action:disabled { opacity: .5; cursor: default; }
  .status { padding: 0 18px 8px; font-size: 12px; color: #93a1af; min-height: 18px; }
  .status.error { color: #e2573f; }
  .status.ok { color: #5bc8b0; }
  .hint { padding: 0 18px 10px; font-size: 11.5px; color: #93a1af; }
  .list { flex: 1; overflow: auto; padding: 0 10px 10px; }
  .row {
    display: grid; grid-template-columns: 2rem minmax(0, 1fr) 7.5rem 4.8rem auto; gap: 12px;
    align-items: center; padding: 9px 10px; border-bottom: 1px solid #1b2634;
  }
  .row:hover { background: #16202c; }
  .row.head {
    color: #6d7887; font-size: 10.5px; letter-spacing: .08em; text-transform: uppercase;
    padding: 2px 10px 6px; border-bottom: 1px solid #24303f;
  }
  .row.head:hover { background: transparent; }
  .rank { color: #6d7887; font-size: 12px; font-variant-numeric: tabular-nums; }
  .details { min-width: 0; }
  .name { font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .address { font-size: 11px; color: #6d7887; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .players, .ping {
    font-size: 11.5px; color: #93a1af; text-align: right; white-space: nowrap;
    font-variant-numeric: tabular-nums;
  }
  .ping.good { color: #5bc8b0; }
  .ping.bad { color: #e2573f; }
  .join {
    background: #1f2c3b; color: #f3e7d0; border: 1px solid #2f4054; border-radius: 4px;
    padding: 6px 12px; font-size: 12px; cursor: pointer;
  }
  .join:hover { border-color: #f2a33c; color: #f2a33c; }
  .empty { padding: 18px; text-align: center; color: #93a1af; font-size: 12.5px; }
  .directory { display: flex; gap: 8px; align-items: center; padding: 10px 18px 12px; border-top: 1px solid #24303f; }
  .directory label { font-size: 11.5px; color: #93a1af; white-space: nowrap; }
  .directory input { font-size: 12px; padding: 6px 8px; }
  footer { padding: 10px 18px 14px; font-size: 11.5px; color: #93a1af; border-top: 1px solid #24303f; }
  [hidden] { display: none !important; }
</style>
</head>
<body>
<div class="panel">
  <header>
    <h1>StarTruckerMP</h1>
    <button class="close" id="close" title="Close">&#10005;</button>
  </header>

  <nav class="tabs" role="tablist">
    <button class="tab active" id="tabDirectory" role="tab" aria-selected="true"></button>
    <button class="tab" id="tabDirect" role="tab" aria-selected="false"></button>
  </nav>

  <section id="directoryView">
    <div class="toolbar">
      <input id="filter" spellcheck="false" autocomplete="off">
      <button class="action" id="refresh"></button>
    </div>
    <div class="status" id="status"></div>
    <div class="list" id="list"></div>
    <div class="directory">
      <label for="url" id="directoryLabel"></label>
      <input id="url" spellcheck="false" autocomplete="off">
    </div>
  </section>

  <section id="directView" hidden>
    <div class="toolbar">
      <input id="ip" spellcheck="false" autocomplete="off">
      <input id="port" class="port" inputmode="numeric" autocomplete="off">
      <button class="action" id="connect"></button>
    </div>
    <div class="status" id="directStatus"></div>
    <div class="hint" id="directHint"></div>
  </section>

  <footer id="footer"></footer>
</div>
<script>
(() => {
  const polish = (navigator.language || '').toLowerCase().startsWith('pl');
  const t = polish ? {
    refresh: 'Odśwież', status: 'Szukam w katalogu...', empty: 'Brak serwerów w katalogu.',
    join: 'Połącz', players: 'graczy', footer: 'Po wybraniu serwera wczytaj zapis, żeby dołączyć.',
    saved: 'Ustawiono serwer', error: 'Błąd', serverColumn: 'Serwer', ping: 'Ping',
    found: 'Serwery: {count}', unknownPing: '?', directoryHint: 'Puste pole czyta adres z konfiguracji.',
    directory: 'Katalog', direct: 'Po adresie IP', directoryLabel: 'Adres katalogu',
    filterHint: 'Szukaj po nazwie serwera', ipHint: 'IP lub host, np. 192.168.1.20',
    connect: 'Połącz', invalid: 'Podaj adres IP lub nazwę hosta.', badPort: 'Port musi być liczbą 1-65535.',
    noMatch: 'Żaden serwer nie pasuje do wpisanej nazwy.',
    directHint: 'Port jest opcjonalny, domyślnie __DEFAULT_PORT__.',
    directReady: 'Gotowe do połączenia. Wczytaj zapis, żeby dołączyć.'
  } : {
    refresh: 'Refresh', status: 'Searching the directory...', empty: 'No servers in the directory.',
    join: 'Connect', players: 'players', footer: 'After picking a server, load a save to connect.',
    saved: 'Server set to', error: 'Error', serverColumn: 'Server', ping: 'Ping',
    found: 'Servers: {count}', unknownPing: '?', directoryHint: 'An empty field reads the configured address.',
    directory: 'Directory', direct: 'By IP address', directoryLabel: 'Directory address',
    filterHint: 'Search by server name', ipHint: 'IP or host, e.g. 192.168.1.20',
    connect: 'Connect', invalid: 'Enter an IP address or host name.', badPort: 'The port must be 1-65535.',
    noMatch: 'No server matches that name.',
    directHint: 'The port is optional, __DEFAULT_PORT__ is used when empty.',
    directReady: 'Ready to connect. Load a save to join.'
  };

  const DEFAULT_PORT = __DEFAULT_PORT__;

  const bridge = window.__starTruckMP = window.__starTruckMP || {};
  bridge.handlers = bridge.handlers || [];
  bridge.dispatchGameMessage = (message) => {
    for (const handler of bridge.handlers) {
      try { handler(message); } catch (error) { console.error(error); }
    }
  };

  const urlInput = document.getElementById('url');
  const filterInput = document.getElementById('filter');
  const refreshButton = document.getElementById('refresh');
  const status = document.getElementById('status');
  const list = document.getElementById('list');
  const footer = document.getElementById('footer');
  const closeButton = document.getElementById('close');
  const tabDirectory = document.getElementById('tabDirectory');
  const tabDirect = document.getElementById('tabDirect');
  const directoryView = document.getElementById('directoryView');
  const directView = document.getElementById('directView');
  const ipInput = document.getElementById('ip');
  const portInput = document.getElementById('port');
  const connectButton = document.getElementById('connect');
  const directStatus = document.getElementById('directStatus');

  let lastServers = [];

  const setStatus = (text, tone) => {
    status.textContent = text || '';
    status.className = 'status' + (tone ? ' ' + tone : '');
  };

  const setDirectStatus = (text, tone) => {
    directStatus.textContent = text || '';
    directStatus.className = 'status' + (tone ? ' ' + tone : '');
  };

  const sendToGame = (type, payload) => {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ type, payload });
      return;
    }
    if (window.CefSharp && window.CefSharp.PostMessage) {
      window.CefSharp.PostMessage({ type, payload });
      return;
    }
    console.warn('[StarTruckerMP] no overlay bridge');
  };

  const refresh = () => {
    // An empty address field is allowed: the plugin then reads the configured directory.
    setStatus(t.status);
    refreshButton.disabled = true;
    sendToGame('multiplayerRefresh', { url: urlInput.value.trim() });
  };

  const join = (server) => {
    sendToGame('joinServer', { host: server.host, port: server.port, name: server.name });
  };

  const matches = (server, query) => {
    if (!query) return true;
    const address = `${server.host}:${server.port}`.toLowerCase();
    return server.name.toLowerCase().includes(query) || address.includes(query);
  };

  const headerRow = () => {
    const head = document.createElement('div');
    head.className = 'row head';
    for (const label of ['#', t.serverColumn, t.players, t.ping]) {
      const cell = document.createElement('div');
      cell.textContent = label;
      head.appendChild(cell);
    }
    head.appendChild(document.createElement('div'));
    return head;
  };

  const render = (servers) => {
    list.replaceChildren();
    const query = filterInput.value.trim().toLowerCase();
    const visible = servers.filter(server => matches(server, query));

    if (!servers || servers.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'empty';
      empty.textContent = t.empty;
      list.appendChild(empty);
      return;
    }

    if (visible.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'empty';
      empty.textContent = t.noMatch;
      list.appendChild(empty);
      return;
    }

    list.appendChild(headerRow());

    visible.forEach((server, index) => {
      const row = document.createElement('div');
      row.className = 'row';

      // The position follows the visible rows, so filtering renumbers the list.
      const rank = document.createElement('div');
      rank.className = 'rank';
      rank.textContent = String(index + 1);

      const details = document.createElement('div');
      details.className = 'details';

      const name = document.createElement('div');
      name.className = 'name';
      name.textContent = server.name;
      if (server.description) name.title = server.description;

      const address = document.createElement('div');
      address.className = 'address';
      address.textContent = `${server.host}:${server.port}`;

      details.append(name, address);

      const players = document.createElement('div');
      players.className = 'players';
      const count = Number.isFinite(server.players) ? server.players : 0;
      players.textContent = server.capacity > 0
        ? `${count}/${server.capacity} ${t.players}`
        : `${count} ${t.players}`;

      const ping = document.createElement('div');
      const pingMs = Number.isFinite(server.ping) ? server.ping : -1;
      ping.className = 'ping' + (pingMs >= 0 ? (pingMs < 150 ? ' good' : ' bad') : '');
      ping.textContent = pingMs >= 0 ? `${pingMs} ms` : t.unknownPing;
      ping.title = pingMs >= 0 ? `${server.host}:${server.port}` : t.ping;

      const button = document.createElement('button');
      button.className = 'join';
      button.textContent = t.join;
      button.addEventListener('click', () => join(server));

      row.append(rank, details, players, ping, button);
      list.appendChild(row);
    });
  };

  const showTab = (direct) => {
    tabDirectory.classList.toggle('active', !direct);
    tabDirect.classList.toggle('active', direct);
    tabDirectory.setAttribute('aria-selected', String(!direct));
    tabDirect.setAttribute('aria-selected', String(direct));
    directoryView.hidden = direct;
    directView.hidden = !direct;
    if (direct) ipInput.focus();
  };

  const connectDirect = () => {
    let host = ipInput.value.trim().replace(/^https?:\/\//i, '').replace(/\/.*$/, '');
    let portText = portInput.value.trim();

    if (!host) { setDirectStatus(t.invalid, 'error'); return; }

    // "host:7777" typed in one field is accepted too.
    const singleColon = (host.match(/:/g) || []).length === 1;
    if (singleColon) {
      const separator = host.lastIndexOf(':');
      const tail = host.slice(separator + 1);
      if (/^\d+$/.test(tail)) {
        host = host.slice(0, separator);
        if (!portText) portText = tail;
      }
    }

    if (!/^[A-Za-z0-9._-]+$/.test(host)) { setDirectStatus(t.invalid, 'error'); return; }

    const port = portText ? Number(portText) : DEFAULT_PORT;
    if (!Number.isInteger(port) || port < 1 || port > 65535) { setDirectStatus(t.badPort, 'error'); return; }

    setDirectStatus(`${t.saved} ${host}:${port}`, 'ok');
    sendToGame('joinServer', { host, port, name: host });
  };

  bridge.handlers.push((message) => {
    if (!message || message.type === undefined) return;
    const payload = message.payload || {};

    if (message.type === 'multiplayerServers') {
      refreshButton.disabled = false;
      lastServers = payload.servers || [];
      if (payload.error) {
        setStatus(t.error + ': ' + payload.error, 'error');
        render([]);
      } else {
        setStatus(t.found.replace('{count}', String(lastServers.length)), 'ok');
        render(lastServers);
      }
    }

    if (message.type === 'serverSelected') {
      if (payload.error) {
        setStatus(t.error + ': ' + payload.error, 'error');
        setDirectStatus(t.error + ': ' + payload.error, 'error');
        return;
      }
      setStatus(`${t.saved} ${payload.host}:${payload.port}`, 'ok');
      setDirectStatus(`${t.saved} ${payload.host}:${payload.port}`, 'ok');
      footer.textContent = t.footer;
    }
  });

  // Messages posted before this page existed wait here, so replay them once.
  const pending = bridge.pendingGameMessages || [];
  bridge.pendingGameMessages = [];
  for (const message of pending) bridge.dispatchGameMessage(message);

  urlInput.placeholder = __DIRECTORY_URL__;
  urlInput.title = t.directoryHint;
  urlInput.setAttribute('aria-label', t.directoryLabel);
  refreshButton.textContent = t.refresh;
  footer.textContent = t.footer;
  filterInput.placeholder = t.filterHint;
  filterInput.setAttribute('aria-label', t.filterHint);
  ipInput.placeholder = t.ipHint;
  ipInput.setAttribute('aria-label', t.ipHint);
  portInput.placeholder = String(DEFAULT_PORT);
  portInput.setAttribute('aria-label', t.directHint);
  connectButton.textContent = t.connect;
  tabDirectory.textContent = t.directory;
  tabDirect.textContent = t.direct;
  document.getElementById('directoryLabel').textContent = t.directoryLabel;
  document.getElementById('directHint').textContent = t.directHint;

  filterInput.addEventListener('input', () => render(lastServers));
  filterInput.addEventListener('keydown', (event) => { if (event.key === 'Enter') refresh(); });
  refreshButton.addEventListener('click', refresh);
  urlInput.addEventListener('keydown', (event) => { if (event.key === 'Enter') refresh(); });
  connectButton.addEventListener('click', connectDirect);
  ipInput.addEventListener('keydown', (event) => { if (event.key === 'Enter') connectDirect(); });
  portInput.addEventListener('keydown', (event) => { if (event.key === 'Enter') connectDirect(); });
  tabDirectory.addEventListener('click', () => showTab(false));
  tabDirect.addEventListener('click', () => showTab(true));
  closeButton.addEventListener('click', () => sendToGame('closeMultiplayer'));
  window.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') sendToGame('closeMultiplayer');
  });

  refresh();
})();
</script>
</body>
</html>
""";
}
