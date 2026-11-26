#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    private static TcpListener? _listener;
    private static int _port;
    private static volatile string _html = "";

    /// <summary>Opens the panel in the overlay, above the running game.</summary>
    public static void Open()
    {
        try
        {
            var url = EnsureLocalPage();
            OverlayManager.Show();
            OverlayManager.Navigate(url);
            OverlayManager.SetInteractiveMode(true);
            App.Log.LogInfo("[Multiplayer] Panel opened.");
        }
        catch (Exception exception)
        {
            // A missing overlay process must never break the game menu.
            App.Log.LogWarning("[Multiplayer] Could not open the panel: " + exception.Message);
        }
    }

    public static void Close()
    {
        try
        {
            OverlayManager.SetInteractiveMode(false);
            OverlayManager.Hide();
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
        var url = App.DirectoryUrl.Value;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(payload, ParseOptions);
                if (document.RootElement.TryGetProperty("url", out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    var typed = value.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(typed))
                        url = typed;
                }
            }
            catch (JsonException)
            {
                // Keep the configured address.
            }
        }

        App.DirectoryUrl.Value = url;
        SaveConfig();

        // Fetching runs off the game thread; the result goes back through the overlay pipe.
        Plugin.StartAttachedThread(() =>
        {
            var (error, servers) = FetchAsync(url).GetAwaiter().GetResult();
            OverlayManager.PostMessage("multiplayerServers", new { url, error, servers });
        });
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
            OverlayManager.PostMessage("serverSelected", new { error = "Invalid server address." });
            return;
        }

        App.ServerAddress.Value = host;
        App.ServerPort.Value = port.ToString();
        SaveConfig();

        App.Log.LogInfo($"[Multiplayer] Selected server {host}:{port} ({name ?? host}). Load a save to connect.");
        OverlayManager.PostMessage("serverSelected", new { host, port, name = name ?? host });
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
                return (null, ParseServers(json));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastError = exception.Message;
            }
        }

        return (lastError ?? "The directory did not answer.", Array.Empty<object>());
    }

    private static object[] ParseServers(string json)
    {
        var servers = new System.Collections.Generic.List<object>();

        using var document = JsonDocument.Parse(json, ParseOptions);
        if (!document.RootElement.TryGetProperty("servers", out var list) ||
            list.ValueKind != JsonValueKind.Array)
            return servers.ToArray();

        foreach (var item in list.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var host = ReadString(item, "host");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
                continue;

            servers.Add(new
            {
                name,
                host,
                port = ReadInt(item, "port"),
                players = ReadInt(item, "players"),
                capacity = ReadInt(item, "capacity"),
                description = ReadString(item, "description")
            });
        }

        return servers.ToArray();
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
    /// The overlay browser renders plain http pages reliably, while a data or custom URL showed
    /// its own error page. A loopback listener avoids the certificate of the game server and keeps
    /// the panel available in the main menu, before any server exists.
    /// </summary>
    private static string EnsureLocalPage()
    {
        _html = BuildHtml();

        if (_listener is null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _listener = listener;
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var thread = new Thread(() => AcceptLoop(listener))
            {
                IsBackground = true,
                Name = "StarTruckerMP.MultiplayerPanel"
            };
            thread.Start();
            App.Log.LogInfo($"[Multiplayer] Panel page served at http://127.0.0.1:{_port}/");
        }

        return $"http://127.0.0.1:{_port}/?v={DateTime.UtcNow.Ticks}";
    }

    private static void AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            try
            {
                var client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
            catch (Exception)
            {
                // The listener is closed when the game exits; nothing else to do here.
                return;
            }
        }
    }

    private static void Serve(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = reader.ReadLine() ?? string.Empty;
                while (!string.IsNullOrEmpty(reader.ReadLine()))
                {
                    // Skip the request headers.
                }

                var isIcon = requestLine.Contains("/favicon", StringComparison.OrdinalIgnoreCase);
                var body = Encoding.UTF8.GetBytes(isIcon ? "" : _html);
                var status = isIcon ? "404 Not Found" : "200 OK";
                var header = $"HTTP/1.1 {status}\r\n" +
                             "Content-Type: text/html; charset=utf-8\r\n" +
                             "Cache-Control: no-store\r\n" +
                             $"Content-Length: {body.Length}\r\n" +
                             "Connection: close\r\n\r\n";

                var headerBytes = Encoding.ASCII.GetBytes(header);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
        }
        catch (Exception)
        {
            // A closed panel must never affect the game.
        }
    }

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
    width: min(760px, 92vw); max-height: 86vh; display: flex; flex-direction: column;
    background: #101823; border: 1px solid #24303f; border-top: 3px solid #f2a33c;
    border-radius: 6px; box-shadow: 0 18px 60px rgb(0 0 0 / 55%);
  }
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
  .list { overflow: auto; padding: 0 10px 10px; }
  .row {
    display: grid; grid-template-columns: 1fr auto auto; gap: 12px; align-items: center;
    padding: 9px 10px; border-bottom: 1px solid #1b2634;
  }
  .row:hover { background: #16202c; }
  .name { font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .meta { font-size: 11.5px; color: #93a1af; white-space: nowrap; }
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
    saved: 'Ustawiono serwer', error: 'Błąd', needsUrl: 'Najpierw wpisz host i port katalogu.',
    directory: 'Katalog', direct: 'Po adresie IP', directoryLabel: 'Adres katalogu',
    filterHint: 'Szukaj po nazwie serwera', ipHint: 'IP lub host, np. 192.168.1.20',
    connect: 'Połącz', invalid: 'Podaj adres IP lub nazwę hosta.', badPort: 'Port musi być liczbą 1-65535.',
    noMatch: 'Żaden serwer nie pasuje do wpisanej nazwy.',
    directHint: 'Port jest opcjonalny, domyślnie __DEFAULT_PORT__.',
    directReady: 'Gotowe do połączenia. Wczytaj zapis, żeby dołączyć.'
  } : {
    refresh: 'Refresh', status: 'Searching the directory...', empty: 'No servers in the directory.',
    join: 'Connect', players: 'players', footer: 'After picking a server, load a save to connect.',
    saved: 'Server set to', error: 'Error', needsUrl: 'Enter the directory host and port first.',
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
    const url = urlInput.value.trim();
    if (!url) { setStatus(t.needsUrl, 'error'); return; }
    setStatus(t.status);
    refreshButton.disabled = true;
    sendToGame('multiplayerRefresh', { url });
  };

  const join = (server) => {
    sendToGame('joinServer', { host: server.host, port: server.port, name: server.name });
  };

  const matches = (server, query) => {
    if (!query) return true;
    const address = `${server.host}:${server.port}`.toLowerCase();
    return server.name.toLowerCase().includes(query) || address.includes(query);
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

    for (const server of visible) {
      const row = document.createElement('div');
      row.className = 'row';

      const name = document.createElement('div');
      name.className = 'name';
      name.textContent = server.name;
      if (server.description) name.title = server.description;

      const meta = document.createElement('div');
      meta.className = 'meta';
      const players = server.capacity > 0 ? ` · ${server.players}/${server.capacity} ${t.players}` : '';
      meta.textContent = `${server.host}:${server.port}${players}`;

      const button = document.createElement('button');
      button.className = 'join';
      button.textContent = t.join;
      button.addEventListener('click', () => join(server));

      row.append(name, meta, button);
      list.appendChild(row);
    }
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
      if (payload.url) urlInput.value = payload.url;
      if (payload.error) {
        setStatus(t.error + ': ' + payload.error, 'error');
        render([]);
      } else {
        setStatus(`${lastServers.length}`, 'ok');
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

  urlInput.value = __DIRECTORY_URL__;
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
