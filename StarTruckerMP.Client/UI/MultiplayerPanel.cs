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

        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            OverlayManager.PostMessage("serverSelected", new { error = "Invalid server address." });
            return;
        }

        App.ServerAddress.Value = host;
        App.ServerPort.Value = port.ToString();
        SaveConfig();

        App.Log.LogInfo($"[Multiplayer] Selected server {host}:{port} ({name}). Load a save to connect.");
        OverlayManager.PostMessage("serverSelected", new { host, port, name });
    }

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
        .Replace("__DIRECTORY_URL__", JsonSerializer.Serialize(App.DirectoryUrl.Value), StringComparison.Ordinal);

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
  .toolbar { display: flex; gap: 8px; padding: 14px 18px 8px; }
  input {
    flex: 1; min-width: 0; background: #0b121b; color: #f3e7d0; border: 1px solid #24303f;
    border-radius: 4px; padding: 8px 10px; font-size: 13px;
  }
  button.action {
    background: #f2a33c; color: #20140a; border: 0; border-radius: 4px;
    padding: 8px 16px; font-weight: 700; cursor: pointer; font-size: 13px;
  }
  button.action:disabled { opacity: .5; cursor: default; }
  .status { padding: 0 18px 8px; font-size: 12px; color: #93a1af; min-height: 18px; }
  .status.error { color: #e2573f; }
  .status.ok { color: #5bc8b0; }
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
  footer { padding: 10px 18px 14px; font-size: 11.5px; color: #93a1af; border-top: 1px solid #24303f; }
  b.host { color: #f3e7d0; font-weight: 600; }
</style>
</head>
<body>
<div class="panel">
  <header>
    <h1>StarTruckerMP</h1>
    <span class="meta" id="lang"></span>
    <button class="close" id="close" title="Close">&#10005;</button>
  </header>
  <div class="toolbar">
    <input id="url" spellcheck="false" autocomplete="off">
    <button class="action" id="refresh"></button>
  </div>
  <div class="status" id="status"></div>
  <div class="list" id="list"></div>
  <footer id="footer"></footer>
</div>
<script>
(() => {
  const polish = (navigator.language || '').toLowerCase().startsWith('pl');
  const t = polish ? {
    refresh: 'Odśwież', status: 'Szukam w katalogu...', empty: 'Brak serwerów w katalogu.',
    join: 'Połącz', players: 'graczy', footer: 'Po wybraniu serwera wczytaj zapis, żeby dołączyć.',
    saved: 'Ustawiono serwer', current: 'Adres katalogu', error: 'Katalog niedostępny',
    needsUrl: 'Najpierw wpisz host i port katalogu.'
  } : {
    refresh: 'Refresh', status: 'Searching the directory...', empty: 'No servers in the directory.',
    join: 'Connect', players: 'players', footer: 'After picking a server, load a save to connect.',
    saved: 'Server set to', current: 'Directory address', error: 'Directory unavailable',
    needsUrl: 'Enter the directory host and port first.'
  };

  const bridge = window.__starTruckMP = window.__starTruckMP || {};
  bridge.handlers = bridge.handlers || [];
  bridge.dispatchGameMessage = (message) => {
    for (const handler of bridge.handlers) {
      try { handler(message); } catch (error) { console.error(error); }
    }
  };

  const urlInput = document.getElementById('url');
  const refreshButton = document.getElementById('refresh');
  const status = document.getElementById('status');
  const list = document.getElementById('list');
  const footer = document.getElementById('footer');
  const closeButton = document.getElementById('close');

  let lastServers = [];

  const setStatus = (text, tone) => {
    status.textContent = text || '';
    status.className = 'status' + (tone ? ' ' + tone : '');
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

  const render = (servers) => {
    list.replaceChildren();
    if (!servers || servers.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'empty';
      empty.textContent = t.empty;
      list.appendChild(empty);
      return;
    }

    for (const server of servers) {
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
        return;
      }
      setStatus(`${t.saved} ${payload.host}:${payload.port}`, 'ok');
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
  document.getElementById('lang').textContent = t.current;
  refreshButton.addEventListener('click', refresh);
  urlInput.addEventListener('keydown', (event) => { if (event.key === 'Enter') refresh(); });
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
