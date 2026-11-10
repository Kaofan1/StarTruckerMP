using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StarTruckerMP.Launcher.Localization;
using StarTruckerMP.Launcher.Services;

namespace StarTruckerMP.Launcher.Mod;

/// <summary>
/// Resolves the BepInEx archive the mod actually needs.
/// Star Trucker is a Unity <b>IL2CPP</b> game, and IL2CPP loaders are not part of the
/// stable BepInEx releases on GitHub (those ship BepInEx 5 for Mono games only).
/// The IL2CPP loader lives in the BepInEx 6 Bleeding Edge builds, so the launcher has
/// to read the build list from builds.bepinex.dev.
/// </summary>
internal static class BepInExSource
{
    public const string BuildsPageUrl = "https://builds.bepinex.dev/projects/bepinex_be";

    private static readonly Regex ZipLinkRegex = new(
        "href=\"(?<href>[^\"]+?\\.zip)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BuildNumberRegex = new(
        "be\\.(?<build>[0-9]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches both naming schemes used by BepInBuilds:
    /// <c>BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793+dbc82f0.zip</c> and the older
    /// <c>BepInEx_UnityIL2CPP_x64_f2c0e0f_6.0.0-be.549.zip</c>.
    /// </summary>
    public static bool IsIl2CppWindowsX64(string fileName) =>
        fileName.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase) &&
        fileName.Contains("win", StringComparison.OrdinalIgnoreCase) &&
        fileName.Contains("x64", StringComparison.OrdinalIgnoreCase) &&
        !fileName.Contains("x86", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the download URL of the newest IL2CPP win-x64 build and its build number.
    /// The build list is ordered newest first, so the first match is the latest build.
    /// </summary>
    public static async Task<(string Url, string Build)> FindLatestAsync(CancellationToken cancellationToken)
    {
        using var response = await Downloader.Client
            .GetAsync(BuildsPageUrl, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        foreach (Match match in ZipLinkRegex.Matches(page))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var fileName = href[(href.LastIndexOf('/') + 1)..];

            if (!IsIl2CppWindowsX64(fileName))
                continue;

            var url = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : new Uri(new Uri(BuildsPageUrl), href).ToString();

            var buildMatch = BuildNumberRegex.Match(fileName);
            var build = buildMatch.Success ? buildMatch.Groups["build"].Value : "unknown";

            return (url, build);
        }

        throw new InvalidOperationException(Strings.BepInExNoBuild);
    }
}
