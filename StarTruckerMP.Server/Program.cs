using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging.Console;
using StarTruckerMP.Server;
using StarTruckerMP.Server.Controllers.Services;
using StarTruckerMP.Server.Crypto;
using StarTruckerMP.Server.Server;
using StarTruckerMP.Server.Server.Services;

internal class Program
{
    public static void Main(string[] args)
    {
        // A service starts in an arbitrary working directory, so every file the server owns is
        // resolved next to the executable: server.json, the certificate and wwwroot.
        var appDirectory = AppContext.BaseDirectory;
        var appConfigPath = Path.Combine(appDirectory, "server.json");
        var appDirectoryWritable = IsDirectoryWritable(appDirectory);
        var sharedDataDirectory = GetSharedDataDirectory();
        var sharedConfigPath = Path.Combine(sharedDataDirectory, "server.json");

        // The Setup keeps the live settings in ProgramData, where the operator can edit them
        // without administrator rights, and that copy wins when it exists. A portable copy or the
        // launcher's own server has none and keeps its settings next to the executable.
        string dataDirectory;
        string settingsPath;

        if (File.Exists(sharedConfigPath))
        {
            dataDirectory = sharedDataDirectory;
            settingsPath = sharedConfigPath;
        }
        else if (appDirectoryWritable)
        {
            dataDirectory = appDirectory;
            settingsPath = appConfigPath;
        }
        else
        {
            dataDirectory = sharedDataDirectory;
            settingsPath = sharedConfigPath;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = appDirectory
        });

        if (!File.Exists(settingsPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                var defaultSettings = new ServerSettings();
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(defaultSettings, App.JsonOptionsWrite));
                Console.WriteLine($"server.json not found, created {settingsPath} with default settings.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A read-only folder must not stop the server; the defaults work for a LAN game.
                Console.WriteLine($"server.json could not be created in {Path.GetDirectoryName(settingsPath)}: {exception.Message}");
            }
        }

        builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        builder.Host
            .UseSystemd()
            .UseWindowsService();

        builder.Logging
            .ClearProviders()
            .AddSimpleConsole(opt =>
            {
                opt.ColorBehavior = LoggerColorBehavior.Enabled;
                opt.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
            })
            .AddFilter("Microsoft.AspNetCore", LogLevel.Warning) // filter out noisy Kestrel logs
#if DEBUG
            .SetMinimumLevel(LogLevel.Information)
#endif
            ;

        builder.WebHost.ConfigureKestrel(options =>
        {
            // Bind HTTP API on the same port number as LiteNetLib (UDP).
            // LiteNetLib uses UDP, Kestrel uses TCP - they can share the same port number.
            var address = IPAddress.TryParse(builder.Configuration.GetValue<string>("IpAddress"), out var ip)
                ? ip
                : IPAddress.Any;
            
            options.ConfigureHttpsDefaults(httpsOptions =>
            {
                var certPath = builder.Configuration["CertificatePath"] ?? "certs/localhost.pfx";
                var certPassword = builder.Configuration["CertificatePassword"] ?? "changeit";

                if (!Path.IsPathRooted(certPath))
                    certPath = Path.Combine(dataDirectory, certPath);

                Directory.CreateDirectory(Path.GetDirectoryName(certPath)!);

                if (!File.Exists(certPath))
                {
                    using var rsa = RSA.Create(2048);

                    var request = new CertificateRequest(
                        "CN=localhost",
                        rsa,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);

                    request.CertificateExtensions.Add(
                        new X509BasicConstraintsExtension(false, false, 0, false));

                    request.CertificateExtensions.Add(
                        new X509KeyUsageExtension(
                            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                            false));

                    request.CertificateExtensions.Add(
                        new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                    var san = new SubjectAlternativeNameBuilder();
                    san.AddDnsName("localhost");
                    san.AddIpAddress(System.Net.IPAddress.Loopback);
                    san.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
                    request.CertificateExtensions.Add(san.Build());

                    using var cert = request.CreateSelfSigned(
                        DateTimeOffset.UtcNow.AddDays(-1),
                        DateTimeOffset.UtcNow.AddYears(1));

                    var pfxBytes = cert.Export(X509ContentType.Pfx, certPassword);
                    File.WriteAllBytes(certPath, pfxBytes);
                }

                httpsOptions.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);
            });
            options.Listen(address, builder.Configuration.GetValue<int>("Port"), listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                listenOptions.UseHttps();
            });
        });

        builder.Services.AddControllers()
            .AddJsonOptions(opt =>
            {
                opt.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                opt.JsonSerializerOptions.WriteIndented = false;
            });

        builder.Services.AddSingleton<ServerDirectory>();
        builder.Services.AddHostedService<DirectoryAnnouncer>();

        builder.Services.AddSingleton<ServerSettings>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<Program>>();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(
                    "Server startup at {DateTime:yyyy-MM-dd HH:mm:ss}, log level {logLevel}",
                    DateTime.Now,
                    logger.IsEnabled(LogLevel.Trace) ? "Trace"
                    : logger.IsEnabled(LogLevel.Debug) ? "Debug"
                    : logger.IsEnabled(LogLevel.Information) ? "Information"
                    : logger.IsEnabled(LogLevel.Warning) ? "Warning"
                    : logger.IsEnabled(LogLevel.Error) ? "Error"
                    : "Critical");

            var serverSettings = File.Exists(settingsPath)
                ? JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(settingsPath), App.JsonOptionsRead) ?? new ServerSettings()
                : new ServerSettings();

            // Rewrite the settings file on every startup so it has all the latest fields and clean
            // formatting. An installed server keeps it in Program Files, which a normal user may
            // only read, so the rewrite is best effort and the server starts either way.
            try
            {
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(serverSettings, App.JsonOptionsWrite));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    "server.json is read only ({Path}), continuing with the values it holds: {Reason}",
                    settingsPath,
                    exception.Message);
            }

            return serverSettings;
        });

        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<PlayerContainer>();
        builder.Services.AddSingleton<ServerKeyPair>();
        builder.Services.AddSingleton<ServerManager>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddHttpClient<XboxTokenValidator>();
        builder.Services.AddHttpClient<SteamTicketValidator>();

        builder.Services.AddHostedService<ServerBackgroundService>();

        var app = builder.Build();

        App.ServiceProvider = app.Services;

        app.UseHttpsRedirection()
            .UseHsts()
            .UseCors(policy =>  policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
        
        // Serve SvelteKit static assets (JS, CSS, fonts, etc.) from wwwroot
        app.UseStaticFiles();

        app.MapControllers();
        
        // The root shows a small status page instead of a bare string, so the server can be checked
        // from a browser: version, online players and the sectors they are in. Player names are
        // personal data, so they only appear for a request that carries a valid session token.
        app.MapGet("/", (HttpContext context, PlayerContainer players, ServerSettings settings, AuthService auth) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var token = StatusPage.ReadToken(context);
            var showPlayers = token is not null && auth.IsTokenValid(token);
            var tokenRejected = !showPlayers && context.Request.Query.ContainsKey(StatusPage.RejectedQuery);

            return Results.Content(
                StatusPage.Render(settings, players, StatusPage.WantsPolish(context), showPlayers, tokenRejected),
                "text/html; charset=utf-8");
        });

        // The token form keeps the token in an HttpOnly cookie, so it stays out of the address bar
        // and out of the page source. The header stays supported for scripts and curl.
        app.MapPost("/status/token", (HttpContext context, AuthService auth) =>
        {
            var token = context.Request.Form[StatusPage.TokenField].ToString().Trim();
            if (token.Length == 0 || !auth.IsTokenValid(token))
            {
                context.Response.Cookies.Delete(StatusPage.TokenCookie);
                return Results.Redirect("/?token=invalid");
            }

            context.Response.Cookies.Append(StatusPage.TokenCookie, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });

            return Results.Redirect("/");
        });

        app.MapPost("/status/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Delete(StatusPage.TokenCookie);
            return Results.Redirect("/");
        });

        // Public directory for dedicated servers: they announce themselves here, the launcher
        // searches it. Any instance can be the hub, so no central service is baked in.
        app.MapGet("/directory/servers", (HttpContext context, ServerDirectory directory) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(directory.Snapshot(context.Request.Query["q"]), ServerDirectory.JsonOptions);
        });

        app.MapPost("/directory/announce", async (HttpContext context, ServerDirectory directory) =>
        {
            AnnounceRequest? request;

            try
            {
                request = await JsonSerializer.DeserializeAsync<AnnounceRequest>(
                    context.Request.Body, App.JsonOptionsRead, context.RequestAborted);
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "invalid json" }, ServerDirectory.JsonOptions, statusCode: 400);
            }

            if (request is null)
                return Results.Json(new { error = "empty body" }, ServerDirectory.JsonOptions, statusCode: 400);

            var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = directory.Announce(request, address);

            object body = result.Error is null
                ? new { id = result.Id, token = result.Token, ttlSeconds = 180 }
                : new { error = result.Error };

            return Results.Json(body, ServerDirectory.JsonOptions, statusCode: result.StatusCode);
        });

        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
        startupLogger.LogInformation("Settings: {SettingsPath}, data directory: {DataDirectory}", settingsPath, dataDirectory);

        var settings = app.Services.GetRequiredService<ServerSettings>();
        var bindAddress = IPAddress.TryParse(settings.IpAddress, out var bindIp) ? bindIp : IPAddress.Any;
        startupLogger.LogInformation("Kestrel HTTP server starting on https://{Address}:{Port}", bindAddress, settings.Port);

        app.Run();
    }

    /// <summary>
    /// Checks for write access before choosing where the certificate lives. A probe file is the
    /// only reliable answer on Windows, where ACLs decide and the folder is not simply read-only.
    /// </summary>
    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");

            using (File.Create(probe))
            {
            }

            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Writable folder for the installed server: ProgramData on Windows, /var/lib elsewhere.</summary>
    private static string GetSharedDataDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "StarTruckerMP");

        return "/var/lib/startruckermp";
    }
}