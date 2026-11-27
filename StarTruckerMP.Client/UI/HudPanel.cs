#nullable enable
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StarTruckerMP.Client.Synchronization;

namespace StarTruckerMP.Client.UI;

/// <summary>
/// The small in-game panel F2 shows: whether the game is connected, how many players are online and
/// which network id this player has. It is a local page like the server browser, so it works in the
/// main menu and in a session, and it stays click-through because it only displays state.
/// </summary>
internal static class HudPanel
{
    private static readonly LocalPageServer Page = new("StarTruckerMP.HudPanel");
    private static readonly HttpClient Client = CreateClient();

    private static volatile bool _visible;
    private static volatile int _players = -1;
    private static volatile int _capacity;
    private static long _nextPlayerPoll;

    private const int PlayerPollIntervalMs = 5000;

    public static bool IsVisible => _visible;

    public static void Toggle()
    {
        if (_visible)
            Hide();
        else
            Show();
    }

    public static void Show()
    {
        try
        {
            var url = Page.Publish(HudHtml);
            OverlayManager.SetInteractiveMode(false);
            OverlayManager.Show();
            OverlayManager.Navigate(url);
            _visible = true;
            App.Log.LogInfo("[Hud] Shown.");
            PushState();
            PollPlayers(force: true);
        }
        catch (Exception exception)
        {
            // A missing overlay process must never break the game.
            App.Log.LogWarning("[Hud] Could not show the panel: " + exception.Message);
        }
    }

    public static void Hide()
    {
        if (!_visible)
            return;

        _visible = false;
        OverlayManager.Hide();
        App.Log.LogInfo("[Hud] Hidden.");
    }

    /// <summary>Another page took over the overlay window.</summary>
    public static void MarkHidden() => _visible = false;

    /// <summary>Messages sent by the HUD page through the overlay pipe.</summary>
    public static void HandleMessage(string type, string? payload)
    {
        if (type == "hudLoaded")
        {
            // The page is ready: it asks for the state instead of waiting for the next change,
            // because the overlay process starts long before the browser finishes loading.
            PushState();
            PollPlayers(force: true);
        }
    }

    /// <summary>Sends the current connection state to the page.</summary>
    public static void PushState()
    {
        var connected = Network.IsConnected;
        if (!connected)
        {
            _players = -1;
            _capacity = 0;
        }

        OverlayManager.PostMessage("hudState", new
        {
            connected,
            server = $"{App.ServerAddress.Value}:{App.ServerPort.Value}",
            netId = connected ? Network.NetId : -1,
            players = _players,
            capacity = _capacity
        });
    }

    /// <summary>Called from the game update loop, so the player count follows the session.</summary>
    public static void Tick()
    {
        if (_visible)
            PollPlayers(force: false);
    }

    private static void PollPlayers(bool force)
    {
        if (!Network.IsConnected)
        {
            if (_players != -1)
                PushState();
            return;
        }

        var now = Environment.TickCount64;
        if (!force && now < Interlocked.Read(ref _nextPlayerPoll))
            return;

        Interlocked.Exchange(ref _nextPlayerPoll, now + PlayerPollIntervalMs);

        Plugin.StartAttachedThread(() =>
        {
            var (players, capacity) = FetchPlayersAsync().GetAwaiter().GetResult();
            if (players < 0)
                return;

            _players = players;
            _capacity = capacity;

            if (_visible)
                PushState();
        });
    }

    /// <summary>The server publishes counts only, so this endpoint needs no session token.</summary>
    private static async Task<(int Players, int Capacity)> FetchPlayersAsync()
    {
        var url = $"https://{App.ServerAddress.Value}:{App.ServerPort.Value}/status/summary";

        try
        {
            using var response = await Client.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (-1, 0);

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("players", out var playersValue) ||
                !playersValue.TryGetInt32(out var players))
                return (-1, 0);

            var capacity = document.RootElement.TryGetProperty("capacity", out var capacityValue) &&
                           capacityValue.TryGetInt32(out var parsed)
                ? parsed
                : 0;

            return (players, capacity);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // An older server has no summary endpoint or is not reachable right now.
            return (-1, 0);
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        // A static TimeSpan field declared after Client would still be zero here, and
        // HttpClient rejects a zero timeout, so the value is written inline.
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    private const string HudHtml = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>StarTruckerMP</title>
<style>
  :root { color-scheme: dark; }
  html, body { margin: 0; padding: 0; background: transparent; overflow: hidden; }
  body { font-family: "Segoe UI", system-ui, sans-serif; color: #f3e7d0; }
  .hud {
    position: fixed; top: 22px; left: 22px; min-width: 250px;
    padding: 12px 16px; background: rgb(9 15 23 / 85%);
    border: 1px solid #24303f; border-left: 3px solid #f2a33c; border-radius: 5px;
    box-shadow: 0 10px 30px rgb(0 0 0 / 45%); font-size: 13px; line-height: 1.7;
  }
  .title { margin-bottom: 6px; color: #f2a33c; font-weight: 700; font-size: 12px;
           letter-spacing: .16em; text-transform: uppercase; }
  .row { display: flex; justify-content: space-between; gap: 24px; }
  .row span { color: #93a1af; }
  .row b { font-weight: 600; font-variant-numeric: tabular-nums; }
  .row b.online { color: #5bc8b0; }
  .row b.offline { color: #e2573f; }
  .hint { margin-top: 8px; padding-top: 6px; border-top: 1px solid #1b2634;
          color: #6d7887; font-size: 11.5px; }
</style>
</head>
<body>
<div class="hud">
  <div class="title">StarTruckerMP</div>
  <div class="row"><span id="lServer"></span><b id="server">-</b></div>
  <div class="row"><span id="lSession"></span><b id="session" class="offline">-</b></div>
  <div class="row"><span id="lPlayers"></span><b id="players">-</b></div>
  <div class="row"><span id="lNetId"></span><b id="netId">-</b></div>
  <div class="hint" id="hint"></div>
</div>
<script>
(() => {
  const polish = (navigator.language || '').toLowerCase().startsWith('pl');
  const t = polish ? {
    server: 'Serwer', session: 'Sesja', online: 'połączono', offline: 'brak połączenia',
    players: 'Gracze', netId: 'Moje ID', hint: 'F2 ukrywa tę planszę.', unknown: '?'
  } : {
    server: 'Server', session: 'Session', online: 'connected', offline: 'disconnected',
    players: 'Players', netId: 'My ID', hint: 'F2 hides this panel.', unknown: '?'
  };

  const bridge = window.__starTruckMP = window.__starTruckMP || {};
  bridge.handlers = bridge.handlers || [];
  bridge.dispatchGameMessage = (message) => {
    for (const handler of bridge.handlers) {
      try { handler(message); } catch (error) { console.error(error); }
    }
  };

  const elements = {
    server: document.getElementById('server'),
    session: document.getElementById('session'),
    players: document.getElementById('players'),
    netId: document.getElementById('netId')
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
  };

  bridge.handlers.push((message) => {
    if (!message || message.type !== 'hudState') return;
    const payload = message.payload || {};

    elements.server.textContent = payload.server || '-';

    const connected = payload.connected === true;
    elements.session.textContent = connected ? t.online : t.offline;
    elements.session.className = connected ? 'online' : 'offline';

    const players = typeof payload.players === 'number' ? payload.players : -1;
    const capacity = typeof payload.capacity === 'number' ? payload.capacity : 0;
    elements.players.textContent = players < 0
      ? t.unknown
      : (capacity > 0 ? `${players}/${capacity}` : String(players));

    const netId = typeof payload.netId === 'number' ? payload.netId : -1;
    elements.netId.textContent = connected && netId >= 0 ? `#${netId}` : t.unknown;
  });

  // Messages posted before this page existed wait here, so replay them once.
  const pending = bridge.pendingGameMessages || [];
  bridge.pendingGameMessages = [];
  for (const message of pending) bridge.dispatchGameMessage(message);

  document.getElementById('lServer').textContent = t.server;
  document.getElementById('lSession').textContent = t.session;
  document.getElementById('lPlayers').textContent = t.players;
  document.getElementById('lNetId').textContent = t.netId;
  document.getElementById('hint').textContent = t.hint;

  sendToGame('hudLoaded', { timestamp: Date.now() });
})();
</script>
</body>
</html>
""";
}
