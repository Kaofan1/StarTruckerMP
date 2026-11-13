using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace StarTruckerMP.Launcher.Services;

internal enum DedicatedServerState
{
    Missing,
    Stopped,
    Running
}

internal sealed record DedicatedServerStatus(
    DedicatedServerState State,
    string? ExecutablePath,
    int ProcessId,
    string? ConfigPath,
    string StatusPageUrl);

/// <summary>Outcome of one start or stop, so the card can report the real reason.</summary>
internal enum ServiceActionResult
{
    Success,
    NeedsElevation,
    Timeout
}

/// <summary>
/// The server from StarTruckerMP-Server-Setup.exe: Program Files plus the StarTruckerMPServer
/// Windows service with settings in ProgramData. It is a separate installation from the
/// launcher's own copy and has its own card, so the two never blur together.
/// </summary>
internal static class DedicatedServer
{
    public const string ServiceName = "StarTruckerMPServer";
    public const string ExecutableName = "StarTruckerMP.Server.exe";

    private const string InstallRegistryKey = @"Software\StarTruckerMP Server";

    /// <summary>Install folder recorded by Setup, with the usual Program Files path as fallback.</summary>
    public static string InstallDirectory
    {
        get
        {
            var recorded = ReadInstallDirectory();
            if (!string.IsNullOrWhiteSpace(recorded))
                return recorded;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "StarTruckerMP Server");
        }
    }

    /// <summary>The live settings, writable for normal users so Notepad can save them.</summary>
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StarTruckerMP",
        "server.json");

    /// <summary>
    /// Address players reach this server at, not localhost: the configured bind address, then the
    /// public DirectoryHost, then the LAN IPv4 of this machine. The status page listens on the
    /// same port as the game.
    /// </summary>
    public static string StatusPageUrl
    {
        get
        {
            var (host, port) = ReadStatusPageTarget();
            return BuildStatusPageUrl(host, port);
        }
    }

    public static DedicatedServerStatus Detect()
    {
        var state = QueryServiceState();
        var executable = ServiceExecutablePath() ?? Path.Combine(InstallDirectory, ExecutableName);

        return state switch
        {
            WindowsServiceState.Running => new DedicatedServerStatus(
                DedicatedServerState.Running, executable, ServiceProcessId(), ConfigPath, StatusPageUrl),
            WindowsServiceState.Stopped => new DedicatedServerStatus(
                DedicatedServerState.Stopped, executable, 0, ConfigPath, StatusPageUrl),
            _ => new DedicatedServerStatus(DedicatedServerState.Missing, null, 0, null, StatusPageUrl)
        };
    }

    /// <summary>
    /// Starts the service. Windows answers access denied to a normal user, so the command is
    /// repeated with the UAC prompt instead of sending the operator to services.msc.
    /// </summary>
    public static ServiceActionResult Start() =>
        RunServiceCommand($"start {ServiceName}", alreadyInState: 1056, target: WindowsServiceState.Running);

    public static ServiceActionResult Stop() =>
        RunServiceCommand($"stop {ServiceName}", alreadyInState: 1062, target: WindowsServiceState.Stopped);

    /// <summary>Opens the ProgramData settings in Notepad; false when Setup never wrote them.</summary>
    public static bool OpenConfig()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
            return false;

        Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"")
        {
            UseShellExecute = true
        });

        return true;
    }

    private static (string Host, int Port) ReadStatusPageTarget()
    {
        var host = string.Empty;
        var port = 7777;

        try
        {
            if (File.Exists(ConfigPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                var root = document.RootElement;

                if (root.TryGetProperty("IpAddress", out var ip) && ip.ValueKind == JsonValueKind.String)
                    host = ip.GetString()?.Trim() ?? string.Empty;

                if (root.TryGetProperty("Port", out var portValue) &&
                    portValue.TryGetInt32(out var parsedPort) && parsedPort is >= 1 and <= 65535)
                    port = parsedPort;

                if (string.IsNullOrEmpty(host) || IsAnyAddress(host))
                {
                    if (root.TryGetProperty("DirectoryHost", out var directoryHost) &&
                        directoryHost.ValueKind == JsonValueKind.String)
                    {
                        var configured = directoryHost.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(configured))
                            host = HostOnly(configured);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or broken server.json falls back to the LAN address below.
        }

        if (string.IsNullOrEmpty(host) || IsAnyAddress(host))
            host = LanAddress() ?? "127.0.0.1";

        return (host, port);
    }

    private static bool IsAnyAddress(string host) =>
        host is "0.0.0.0" or "::" or "[::]";

    /// <summary>A DirectoryHost may carry a scheme or a path; the page only needs its host.</summary>
    private static string HostOnly(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal))
            return value.TrimEnd('/');

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed) ? parsed.Host : value;
    }

    private static string? LanAddress()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static string BuildStatusPageUrl(string host, int port)
    {
        var value = host.Trim();
        if (value.Contains(':') && !value.StartsWith("[", StringComparison.Ordinal))
            value = $"[{value}]";

        return $"https://{value}:{port}/";
    }

    private static string ReadInstallDirectory()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(InstallRegistryKey);
            return key?.GetValue("InstallDir") as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int ServiceProcessId()
    {
        try
        {
            // The service runs in session 0, which is what separates it from the launcher's copy.
            return Process.GetProcessesByName(LocalServer.ProcessName)
                .FirstOrDefault(process => IsServiceProcess(process))?.Id ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsServiceProcess(Process process)
    {
        try
        {
            return process.SessionId == 0;
        }
        catch
        {
            return false;
        }
    }

    private enum WindowsServiceState
    {
        NotInstalled,
        Stopped,
        Running
    }

    private static WindowsServiceState QueryServiceState()
    {
        if (!OperatingSystem.IsWindows())
            return WindowsServiceState.NotInstalled;

        var (exitCode, output) = RunServiceControl($"query {ServiceName}");
        if (exitCode != 0)
            return WindowsServiceState.NotInstalled;

        // sc answers STOP_PENDING or START_PENDING during a transition. Treating those as
        // "not installed" hid the buttons and showed a wrong state in the card.
        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            return WindowsServiceState.Stopped;

        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("PENDING", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("PAUSED", StringComparison.OrdinalIgnoreCase))
            return WindowsServiceState.Running;

        return WindowsServiceState.NotInstalled;
    }

    private static ServiceActionResult RunServiceCommand(
        string arguments,
        int alreadyInState,
        WindowsServiceState target)
    {
        var (exitCode, _) = RunServiceControl(arguments);

        if (exitCode == 5)
        {
            var elevated = RunServiceControlElevated(arguments);
            if (!elevated.Ran)
                return ServiceActionResult.NeedsElevation;

            exitCode = elevated.ExitCode;
        }

        if (exitCode == 5)
            return ServiceActionResult.NeedsElevation;

        if (exitCode != 0 && exitCode != alreadyInState)
            return ServiceActionResult.Timeout;

        return WaitForService(target) ? ServiceActionResult.Success : ServiceActionResult.Timeout;
    }

    private static (bool Ran, int ExitCode) RunServiceControlElevated(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null)
                return (false, 5);

            if (!process.WaitForExit(30000))
                return (true, -1);

            return (true, process.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The elevation prompt was declined or cancelled, which is a normal answer.
            return (false, 5);
        }
        catch
        {
            return (false, 5);
        }
    }

    private static bool WaitForService(WindowsServiceState expected)
    {
        // The server shuts down gracefully, which can take a few seconds, so give it 15 seconds.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            Thread.Sleep(250);
            if (QueryServiceState() == expected)
                return true;
        }

        return QueryServiceState() == expected;
    }

    private static (int ExitCode, string Output) RunServiceControl(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return (-1, string.Empty);

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Nothing to do; the caller only needs the timeout signal.
                }

                return (-1, output);
            }

            return (process.ExitCode, output);
        }
        catch
        {
            return (-1, string.Empty);
        }
    }

    private static string? ServiceExecutablePath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");

            if (key?.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
                return null;

            imagePath = imagePath.Trim();
            return imagePath.StartsWith('"') ? imagePath.Trim('"') : imagePath;
        }
        catch
        {
            return null;
        }
    }
}
