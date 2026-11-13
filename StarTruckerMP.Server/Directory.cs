using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace StarTruckerMP.Server;

/// <summary>What a dedicated server posts to keep itself listed.</summary>
internal sealed record AnnounceRequest(
    string? Id,
    string? Token,
    string? Name,
    string? Host,
    int Port,
    int Players,
    int Capacity,
    string? Description,
    string? Map,
    string? Protocol,
    string? Version);

/// <summary>Outcome of an announce call: the caller only needs an id and a token to stay listed.</summary>
internal sealed record AnnounceResult(int StatusCode, string? Id, string? Token, string? Error);

/// <summary>
/// Public list of dedicated servers. Any running server can host it: dedicated servers post a
/// heartbeat every minute or so, the launcher searches the list. Entries expire after three minutes
/// without a beat, so a crashed server drops out on its own.
/// </summary>
internal sealed class ServerDirectory
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(1);
    private const int MaxEntries = 500;

    private sealed class Stored
    {
        public required string Id { get; init; }
        public required string Token { get; init; }
        public required string Name { get; set; }
        public required string Host { get; set; }
        public int Port { get; set; }
        public int Players { get; set; }
        public int Capacity { get; set; }
        public string? Description { get; set; }
        public string? Map { get; set; }
        public string? Protocol { get; set; }
        public string? Version { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    private readonly ConcurrentDictionary<string, Stored> _servers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAnnounce = new();

    /// <summary>
    /// Creates or refreshes an entry. A new server gets an id and a token; later calls must send both,
    /// so nobody can hijack or overwrite somebody else's listing.
    /// </summary>
    public AnnounceResult Announce(AnnounceRequest request, string clientAddress)
    {
        Cleanup();

        var now = DateTime.UtcNow;
        if (_lastAnnounce.TryGetValue(clientAddress, out var last) && now - last < AnnounceInterval)
            return new AnnounceResult(429, null, null, "announce rate limited");

        _lastAnnounce[clientAddress] = now;

        var name = Sanitize(request.Name, 64);
        var host = Sanitize(request.Host, 255);
        var description = Sanitize(request.Description, 200);
        var map = Sanitize(request.Map, 64);
        var protocol = Sanitize(request.Protocol, 32);
        var version = Sanitize(request.Version, 32);

        if (name is null)
            return new AnnounceResult(400, null, null, "name is required");

        if (host is null || host.Contains(' ') || host.Contains('/'))
            return new AnnounceResult(400, null, null, "host is required");

        if (request.Port is < 1 or > 65535)
            return new AnnounceResult(400, null, null, "port must be between 1 and 65535");

        if (request.Capacity is < 1 or > 256)
            return new AnnounceResult(400, null, null, "capacity must be between 1 and 256");

        if (request.Players < 0 || request.Players > request.Capacity)
            return new AnnounceResult(400, null, null, "players must fit inside capacity");

        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Token))
        {
            if (_servers.Count >= MaxEntries)
                return new AnnounceResult(503, null, null, "directory is full");

            var entry = new Stored
            {
                Id = Guid.NewGuid().ToString("N"),
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                Name = name,
                Host = host,
                Port = request.Port,
                Players = request.Players,
                Capacity = request.Capacity,
                Description = description,
                Map = map,
                Protocol = protocol,
                Version = version,
                LastSeenUtc = now
            };

            _servers[entry.Id] = entry;
            return new AnnounceResult(200, entry.Id, entry.Token, null);
        }

        if (!_servers.TryGetValue(request.Id, out var existing) || !TokenMatches(existing.Token, request.Token))
            return new AnnounceResult(403, null, null, "unknown id or wrong token");

        existing.Name = name;
        existing.Host = host;
        existing.Port = request.Port;
        existing.Players = request.Players;
        existing.Capacity = request.Capacity;
        existing.Description = description;
        existing.Map = map;
        existing.Protocol = protocol;
        existing.Version = version;
        existing.LastSeenUtc = now;

        return new AnnounceResult(200, existing.Id, existing.Token, null);
    }

    /// <summary>Public search; players and capacity come from the last heartbeat.</summary>
    public object Snapshot(string? query)
    {
        Cleanup();
        var now = DateTime.UtcNow;
        var filter = Sanitize(query, 64);

        var servers = _servers.Values
            .Where(server => filter is null ||
                             server.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                             server.Host.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                             (server.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(server => server.Players)
            .ThenBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .Select(server => new
            {
                server.Id,
                server.Name,
                server.Host,
                server.Port,
                server.Players,
                server.Capacity,
                server.Description,
                server.Map,
                server.Protocol,
                server.Version,
                lastSeenSeconds = (int)(now - server.LastSeenUtc).TotalSeconds
            })
            .ToList();

        return new
        {
            total = servers.Count,
            ttlSeconds = (int)Lifetime.TotalSeconds,
            servers
        };
    }

    private void Cleanup()
    {
        var now = DateTime.UtcNow;

        foreach (var (id, entry) in _servers)
        {
            if (now - entry.LastSeenUtc > Lifetime)
                _servers.TryRemove(id, out _);
        }

        foreach (var (address, seen) in _lastAnnounce)
        {
            if (now - seen > TimeSpan.FromMinutes(10))
                _lastAnnounce.TryRemove(address, out _);
        }
    }

    private static bool TokenMatches(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(provided));

    /// <summary>Trims and caps a text field; an empty or blank value becomes null.</summary>
    private static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
