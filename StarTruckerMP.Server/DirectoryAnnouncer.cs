using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StarTruckerMP.Server.Server.Services;
using StarTruckerMP.Shared;

namespace StarTruckerMP.Server;

/// <summary>
/// Keeps this server listed in a public directory: the launcher searches that list, so a dedicated
/// server becomes findable without the player typing its address. The heartbeat runs once a minute
/// against <c>DirectoryUrl</c> from server.json; entries expire after three minutes, so a crashed
/// server drops out on its own.
/// </summary>
internal sealed class DirectoryAnnouncer : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly string ServerVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    private readonly ServerSettings _settings;
    private readonly PlayerContainer _players;
    private readonly ILogger<DirectoryAnnouncer> _logger;
    private readonly HttpClient _client;

    private string? _id;
    private string? _token;
    private string? _lastMessage;

    public DirectoryAnnouncer(ServerSettings settings, PlayerContainer players, ILogger<DirectoryAnnouncer> logger)
    {
        _settings = settings;
        _players = players;
        _logger = logger;

        _client = new HttpClient(new HttpClientHandler
        {
            // A hub usually runs on the self-signed certificate the server creates on first start,
            // and only the public listing is exchanged here.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        })
        {
            Timeout = Timeout
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (IsAnnounceDisabled(_settings.DirectoryUrl))
        {
            _logger.LogInformation(
                "Directory announce is off (DirectoryUrl is {Value})", _settings.DirectoryUrl);
            return;
        }

        // An installed server publishes itself without any configuration: it announces to the
        // directory it hosts, so the launcher pointed at that address lists it right away.
        var baseUrl = NormalizeBaseUrl(_settings.DirectoryUrl) ?? BuiltInHubUrl();

        _logger.LogInformation(
            string.IsNullOrWhiteSpace(_settings.DirectoryUrl)
                ? "Announcing this server to its own directory at {Directory} every minute"
                : "Announcing this server to {Directory} every minute",
            baseUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            await AnnounceAsync(baseUrl, stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One heartbeat. The first call registers and keeps the id and token for later beats.</summary>
    private async Task AnnounceAsync(string baseUrl, CancellationToken cancellationToken, bool canRetry = true)
    {
        var name = string.IsNullOrWhiteSpace(_settings.DirectoryName)
            ? $"{Environment.MachineName} StarTruckerMP"
            : _settings.DirectoryName.Trim();

        var host = ResolveHost();
        var port = (int)_settings.Port;

        var payload = new
        {
            id = _id,
            token = _token,
            name,
            host,
            port,
            players = _players.SnapshotPlayers().Length,
            capacity = (int)_settings.MaxPlayers,
            description = string.IsNullOrWhiteSpace(_settings.DirectoryDescription)
                ? null
                : _settings.DirectoryDescription.Trim(),
            protocol = NetProtocol.CurrentVersion.ToString(),
            version = ServerVersion
        };

        try
        {
            using var response = await _client
                .PostAsJsonAsync($"{baseUrl}/directory/announce", payload, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode && TryReadRegistration(body, out var id, out var token))
            {
                _id = id;
                _token = token;
                Report($"Listed in the directory as {name} ({host}:{port})");
                return;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // The hub restarted or dropped the entry, so the old token is worthless. Register
                // again right away instead of waiting a whole minute to be findable.
                _id = null;
                _token = null;
                Report("The directory no longer knows this listing, registering again");

                if (canRetry)
                {
                    // The hub accepts one announce per second from an address, so the registration
                    // that follows a rejected token waits a moment instead of being rate limited.
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    await AnnounceAsync(baseUrl, cancellationToken, canRetry: false).ConfigureAwait(false);
                }

                return;
            }

            Report($"The directory answered HTTP {(int)response.StatusCode}: {Trim(body)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Report($"The directory is unreachable: {exception.Message}");
        }
    }

    /// <summary>The address players should use. A public IP or domain belongs in DirectoryHost.</summary>
    private string ResolveHost()
    {
        var configured = _settings.DirectoryHost?.Trim();
        if (!string.IsNullOrEmpty(configured))
            return configured;

        var bound = _settings.IpAddress?.Trim();
        if (!string.IsNullOrEmpty(bound) &&
            bound is not "0.0.0.0" and not "::" and not "[::]")
            return bound;

        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString() ?? "127.0.0.1";
        }
        catch (SocketException)
        {
            return "127.0.0.1";
        }
    }

    /// <summary>The built-in hub is this instance, so a fresh install lists itself.</summary>
    private string BuiltInHubUrl()
    {
        var bound = _settings.IpAddress?.Trim();
        var host = !string.IsNullOrEmpty(bound) && bound is not "0.0.0.0" and not "::" and not "[::]"
            ? bound
            : "127.0.0.1";

        if (host.Contains(':') && !host.StartsWith("[", StringComparison.Ordinal))
            host = $"[{host}]";

        return $"https://{host}:{_settings.Port}";
    }

    /// <summary>An operator can turn the announce off with <c>DirectoryUrl</c> set to off or none.</summary>
    private static bool IsAnnounceDisabled(string? url) =>
        url?.Trim().ToLowerInvariant() is "off" or "none" or "disabled";

    /// <summary>Turns user input into a base URL; a missing scheme means https, like the launcher does.</summary>
    private static string? NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim().TrimEnd('/');
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out _) ? trimmed : null;
    }

    private static bool TryReadRegistration(string body, out string? id, out string? token)
    {
        id = null;
        token = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("id", out var idValue) ||
                !document.RootElement.TryGetProperty("token", out var tokenValue))
                return false;

            id = idValue.GetString();
            token = tokenValue.GetString();
            return !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(token);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Logs once per state change, so a hub that stays down does not fill the log every minute.</summary>
    private void Report(string message)
    {
        if (message == _lastMessage)
            return;

        _lastMessage = message;
        _logger.LogInformation("{Message}", message);
    }

    private static string Trim(string body)
    {
        var text = body.Trim();
        return text.Length <= 200 ? text : text[..200] + "…";
    }
}
