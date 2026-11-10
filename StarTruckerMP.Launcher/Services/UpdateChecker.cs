using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StarTruckerMP.Launcher.Services;

/// <summary>A newer launcher release found on GitHub.</summary>
internal sealed record LauncherUpdate(
    string Tag,
    string Name,
    int Number,
    string PageUrl,
    string? PortableUrl,
    string? InstallerUrl);

/// <summary>
/// Checks GitHub releases for a newer launcher build. CI stamps the launcher assembly as
/// <c>1.0.&lt;run number&gt;</c> and publishes it under the tag <c>alpha-v&lt;run number&gt;</c>,
/// so the release number is compared with the build part of the running assembly.
/// A locally built launcher (1.0.0) simply reports the newest published release.
/// </summary>
internal static class UpdateChecker
{
    public const string ReleasesPageUrl = "https://github.com/Kaofan1/StarTruckerMP/releases";

    /// <summary>Asset with the portable launcher, used for an in-place update.</summary>
    public const string PortableAssetName = "StarTruckerMP-Launcher-win-x64.zip";

    /// <summary>Fallback asset when the launcher folder cannot be written in place.</summary>
    public const string InstallerAssetName = "StarTruckerMP-Setup.exe";

    private const string RequestUri = "https://api.github.com/repos/Kaofan1/StarTruckerMP/releases?per_page=30";

    private static readonly Regex TagRegex = new(
        @"^alpha-v(?<number>[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Version CurrentVersion => typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(1, 0, 0);

    /// <summary>
    /// Returns the newest release newer than this build, or null when the launcher is current.
    /// Throws on transport/API errors so the caller can log a useful message.
    /// </summary>
    public static async Task<LauncherUpdate?> FindUpdateAsync(CancellationToken cancellationToken)
    {
        using var response = await Downloader.Client
            .GetAsync(RequestUri, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode} for the release list.");

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        var current = CurrentVersion.Build;
        LauncherUpdate? newest = null;

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                continue;

            var tag = release.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            if (tag is null)
                continue;

            var match = TagRegex.Match(tag);
            if (!match.Success || !int.TryParse(match.Groups["number"].Value, out var number) || number <= current)
                continue;

            if (newest is not null && number <= newest.Number)
                continue;

            var name = release.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var pageUrl = release.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;

            var (portableUrl, installerUrl) = ReadAssetUrls(release);

            newest = new LauncherUpdate(
                tag,
                string.IsNullOrWhiteSpace(name) ? tag : name,
                number,
                string.IsNullOrWhiteSpace(pageUrl) ? ReleasesPageUrl : pageUrl,
                portableUrl,
                installerUrl);
        }

        return newest;
    }

    private static (string? Portable, string? Installer) ReadAssetUrls(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null);

        string? portable = null;
        string? installer = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            if (string.Equals(name, PortableAssetName, StringComparison.OrdinalIgnoreCase))
                portable = url;
            else if (string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                installer = url;
        }

        return (portable, installer);
    }
}
