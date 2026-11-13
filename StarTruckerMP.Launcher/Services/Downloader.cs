using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StarTruckerMP.Launcher.Services;

internal static class Downloader
{
    private const int BufferSize = 81920;

    private static readonly HttpClient SharedClient = CreateHttpClient();

    public static HttpClient Client => SharedClient;

    public static async Task<string> DownloadToTempAsync(string url, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "StarTruckerMP");
        Directory.CreateDirectory(directory);

        var targetPath = Path.Combine(directory, $"{Guid.NewGuid():N}.zip");

        using var response = await SharedClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(targetPath);

        var buffer = new byte[BufferSize];
        long receivedBytes = 0;
        var lastReportedPercent = -1;

        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            receivedBytes += read;

            if (totalBytes <= 0)
                continue;

            var percent = (int)(receivedBytes * 100 / totalBytes);
            if (percent == lastReportedPercent || percent % 10 != 0)
                continue;

            lastReportedPercent = percent;
            progress?.Report($"… {percent}%");
        }

        return targetPath;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StarTruckerMP-Launcher/1.0 (+https://github.com/Kaofan1/StarTruckerMP)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

/// <summary>A release asset together with the tag of the release that carries it.</summary>
internal sealed record ReleaseAsset(string Tag, string Name, string Url);

internal static class GitHubReleases
{
    /// <summary>
    /// Walks the most recent releases of a repository and returns the first asset
    /// whose file name matches <paramref name="assetFilter"/>. Drafts are skipped.
    /// </summary>
    public static async Task<string?> FindAssetUrlAsync(
        string owner,
        string repository,
        Func<string, bool> assetFilter,
        CancellationToken cancellationToken) =>
        (await FindLatestAssetAsync(owner, repository, assetFilter, cancellationToken).ConfigureAwait(false))?.Url;

    /// <summary>
    /// Same walk as <see cref="FindAssetUrlAsync"/>, but also reports the release tag, so a
    /// caller can tell whether the file it already has is the newest one.
    /// </summary>
    public static async Task<ReleaseAsset?> FindLatestAssetAsync(
        string owner,
        string repository,
        Func<string, bool> assetFilter,
        CancellationToken cancellationToken)
    {
        var requestUri = $"https://api.github.com/repos/{owner}/{repository}/releases?per_page=20";

        using var response = await Downloader.Client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = System.Text.Json.JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == System.Text.Json.JsonValueKind.True)
                continue;

            var tag = release.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;

            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != System.Text.Json.JsonValueKind.Array)
                continue;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || !assetFilter(name))
                    continue;

                var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
                if (!string.IsNullOrWhiteSpace(url))
                    return new ReleaseAsset(string.IsNullOrWhiteSpace(tag) ? name : tag, name, url);
            }
        }

        return null;
    }
}
