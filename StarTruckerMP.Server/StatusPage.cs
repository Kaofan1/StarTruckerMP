using System.Net;
using System.Reflection;
using System.Text;
using StarTruckerMP.Server.Entities;
using StarTruckerMP.Server.Server.Services;
using StarTruckerMP.Shared;

namespace StarTruckerMP.Server;

/// <summary>
/// Status page served at the root of the server. It reports only aggregates: the version, how many
/// players are connected and which sectors they are in. Names, addresses and tokens never appear
/// here, so the page can stay open without a session token.
/// </summary>
internal static class StatusPage
{
    private static readonly string ServerVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>The page follows the browser language: Polish for a Polish Accept-Language, else English.</summary>
    public static bool WantsPolish(HttpContext context) =>
        context.Request.Headers.AcceptLanguage.ToString()
            .TrimStart()
            .StartsWith("pl", StringComparison.OrdinalIgnoreCase);

    public static string Render(ServerSettings settings, PlayerContainer players, bool polish)
    {
        var online = new List<Player>();
        foreach (var player in players.SnapshotPlayers())
        {
            if (player.HandshakeCompleted)
                online.Add(player);
        }

        var sectors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in online)
        {
            var sector = string.IsNullOrWhiteSpace(player.Sector) ? "none" : player.Sector;
            sectors[sector] = sectors.TryGetValue(sector, out var count) ? count + 1 : 1;
        }

        var orderedSectors = sectors
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var html = new StringBuilder(4096);

        html.Append("<!DOCTYPE html><html lang=\"").Append(polish ? "pl" : "en").Append("\"><head>");
        html.Append("<meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<meta http-equiv=\"refresh\" content=\"5\">");
        html.Append("<title>StarTruckerMP - ").Append(polish ? "status serwera" : "server status").Append("</title>");
        html.Append("<style>");
        html.Append(":root{color-scheme:dark}");
        html.Append("body{margin:0;padding:48px 20px;background:#11151b;color:#e6edf3;");
        html.Append("font:15px/1.55 'Segoe UI',system-ui,sans-serif}");
        html.Append("main{max-width:640px;margin:0 auto}");
        html.Append("h1{margin:0;font-size:26px;letter-spacing:.02em}");
        html.Append("h1 span{color:#f0a33c}");
        html.Append(".sub{margin:4px 0 28px;color:#8b98a9}");
        html.Append(".card{background:#171d26;border:1px solid #232c3a;border-radius:12px;padding:6px 20px}");
        html.Append("dl{margin:0}");
        html.Append("dl div{display:flex;justify-content:space-between;gap:16px;padding:12px 0;");
        html.Append("border-bottom:1px solid #232c3a}");
        html.Append("dl div:last-child{border-bottom:none}");
        html.Append("dt{color:#8b98a9}");
        html.Append("dd{margin:0;font-variant-numeric:tabular-nums;text-align:right}");
        html.Append(".ok{color:#5fd08a}");
        html.Append("h2{margin:28px 0 10px;font-size:15px;text-transform:uppercase;");
        html.Append("letter-spacing:.08em;color:#8b98a9}");
        html.Append("table{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums}");
        html.Append("th,td{text-align:left;padding:9px 0;border-bottom:1px solid #232c3a}");
        html.Append("th{font-weight:500;color:#8b98a9}");
        html.Append("td:last-child,th:last-child{text-align:right}");
        html.Append("tr:last-child td{border-bottom:none}");
        html.Append(".empty{color:#8b98a9;padding:12px 0}");
        html.Append("footer{margin-top:22px;color:#6d7887;font-size:13px}");
        html.Append("</style></head><body><main>");

        html.Append("<h1>StarTrucker<span>MP</span></h1>");
        html.Append("<p class=\"sub\">").Append(polish ? "Status serwera" : "Server status").Append("</p>");

        html.Append("<div class=\"card\"><dl>");
        AppendRow(html, polish ? "Stan" : "State",
            polish ? "<span class=\"ok\">Działa</span>" : "<span class=\"ok\">Running</span>");
        AppendRow(html, polish ? "Wersja serwera" : "Server version", WebUtility.HtmlEncode(ServerVersion));
        AppendRow(html, polish ? "Wersja protokołu" : "Protocol version",
            $"{NetProtocol.CurrentVersion} ({WebUtility.HtmlEncode((polish ? "min. " : "min. ") + NetProtocol.MinSupportedVersion)})");
        AppendRow(html, polish ? "Gracze" : "Players",
            (polish ? $"{online.Count} z {settings.MaxPlayers}" : $"{online.Count} of {settings.MaxPlayers}"));
        AppendRow(html, polish ? "Port" : "Port", settings.Port.ToString());
        html.Append("</dl></div>");

        html.Append("<h2>").Append(polish ? "Sektory" : "Sectors").Append("</h2>");

        if (orderedSectors.Length == 0)
        {
            html.Append("<p class=\"empty\">")
                .Append(polish ? "Brak graczy w sektorach." : "No players in a sector yet.")
                .Append("</p>");
        }
        else
        {
            html.Append("<div class=\"card\"><table><tr><th>")
                .Append(polish ? "Sektor" : "Sector")
                .Append("</th><th>")
                .Append(polish ? "Gracze" : "Players")
                .Append("</th></tr>");

            foreach (var pair in orderedSectors)
            {
                // Players who are connected but not in a sector (main menu, loading) report "none".
                var name = string.Equals(pair.Key, "none", StringComparison.OrdinalIgnoreCase)
                    ? (polish ? "(poza sektorem)" : "(no sector)")
                    : pair.Key;

                html.Append("<tr><td>").Append(WebUtility.HtmlEncode(name))
                    .Append("</td><td>").Append(pair.Value).Append("</td></tr>");
            }

            html.Append("</table></div>");
        }

        html.Append("<footer>")
            .Append(polish ? "Strona odświeża się co 5 sekund - " : "This page refreshes every 5 seconds - ")
            .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" UTC</footer>");

        html.Append("</main></body></html>");

        return html.ToString();
    }

    private static void AppendRow(StringBuilder html, string label, string value)
    {
        html.Append("<div><dt>").Append(WebUtility.HtmlEncode(label))
            .Append("</dt><dd>").Append(value).Append("</dd></div>");
    }
}
