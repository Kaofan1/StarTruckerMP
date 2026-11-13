using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StarTruckerMP.Launcher.Services;

/// <summary>One public dedicated server as the directory describes it.</summary>
internal sealed record DirectoryServer(
    string Id,
    string Name,
    string Host,
    int Port,
    int Players,
    int Capacity,
    string? Description,
    int LastSeenSeconds);

/// <summary>
/// Reads the public server directory (<c>GET /directory/servers</c>) hosted by any server instance.
/// The hub may run on a self-signed certificate, so certificate errors are accepted here: the data is
/// public and read-only, and nothing secret is sent.
/// </summary>
internal static class ServerDirectoryClient
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<(string? Error, IReadOnlyList<DirectoryServer> Servers)> SearchAsync(
        string? directoryUrl,
        string? query,
        CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(directoryUrl);
        if (baseUrl is null)
            return (Localization.Strings.DirectoryNeedsUrl, Array.Empty<DirectoryServer>());

        var path = "/directory/servers";
        if (!string.IsNullOrWhiteSpace(query))
            path += "?q=" + Uri.EscapeDataString(query.Trim());

        string? lastError = null;

        foreach (var candidate in Candidates(baseUrl + path))
        {
            try
            {
                using var response = await Client.GetAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)response.StatusCode}";
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (null, Parse(json));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastError = exception.Message;
            }
        }

        return (lastError ?? Localization.Strings.DirectoryUnreachable, Array.Empty<DirectoryServer>());
    }

    /// <summary>
    /// Round trip of a plain status request, which is what a browser-like server list calls ping.
    /// The hub answers on https with its self-signed certificate, so plain http is the fallback.
    /// </summary>
    public static async Task<int> MeasurePingAsync(string host, int port, CancellationToken cancellationToken)
    {
        foreach (var scheme in new[] { "https", "http" })
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var response = await PingClient
                    .GetAsync($"{scheme}://{host}:{port}/", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return (int)stopwatch.ElapsedMilliseconds;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Try the other scheme; an address that answers neither shows as unknown.
            }
        }

        return -1;
    }

    /// <summary>Turns user input into a base URL; missing scheme means https.</summary>
    public static string? NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim().TrimEnd('/');
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) ? trimmed : null;
    }

    /// <summary>The default server certificate is self-signed, so plain https often needs a retry.</summary>
    private static IEnumerable<string> Candidates(string url)
    {
        yield return url;

        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            yield return "http://" + url["https://".Length..];
    }

    private static IReadOnlyList<DirectoryServer> Parse(string json)
    {
        var servers = new List<DirectoryServer>();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("servers", out var list) ||
            list.ValueKind != JsonValueKind.Array)
            return servers;

        foreach (var item in list.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var name = ReadString(item, "name");
            var host = ReadString(item, "host");
            if (id is null || name is null || host is null)
                continue;

            servers.Add(new DirectoryServer(
                id,
                name,
                host,
                ReadInt(item, "port"),
                ReadInt(item, "players"),
                ReadInt(item, "capacity"),
                ReadString(item, "description"),
                ReadInt(item, "lastSeenSeconds")));
        }

        return servers;
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static HttpClient PingClient { get; } = CreatePingClient();

    private static HttpClient CreatePingClient()
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(4);
        return client;
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }
}
