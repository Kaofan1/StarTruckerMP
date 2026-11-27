#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace StarTruckerMP.Client.UI;

/// <summary>
/// One overlay process and the pipe that controls it. The game runs two of them: the HUD instance
/// (the Svelte page and the F2 panel) and the Multiplayer instance (the server browser). They are
/// separate processes with separate pipes and windows, so opening one can never replace the page
/// of the other.
/// </summary>
internal sealed class OverlayHandle
{
    private const int PipeConnectTimeoutMs = 2000;
    private const int PipeSendDeadlineMs = 20_000;

    /// <summary>
    /// The session token has to survive the whole overlay cold start: on this machine the pipe
    /// server appeared around twenty seconds after the process started.
    /// </summary>
    private const int PipeTokenDeadlineMs = 120_000;

    private readonly string _pipeName;
    private readonly string _tag;
    private Process? _process;
    private bool _launchStarted;

    public OverlayHandle(string pipeName, string tag)
    {
        _pipeName = pipeName;
        _tag = tag;
    }

    public string PipeName => _pipeName;

    /// <summary>Starts this overlay process once; a second call is ignored.</summary>
    public void Launch()
    {
        if (_launchStarted)
            return;

        _launchStarted = true;

        Plugin.StartAttachedThread(() =>
        {
            _process = OverlayManager.StartOverlayProcess(_pipeName, _tag);
        });
    }

    public void Shutdown()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill();
        }
        catch
        {
            // The process is already gone.
        }
    }

    /// <summary>Switches this overlay between click-through and input capture.</summary>
    public void SetInteractiveMode(bool enabled) => SendCommand($"INTERACTIVE:{(enabled ? "1" : "0")}");

    public void RunDiagnostics() => SendCommand("RUNTESTS");

    public void Navigate(string url) => SendCommand($"NAVIGATE:{url}");

    /// <summary>Loads raw HTML into the overlay.</summary>
    public void NavigateToHtml(string html) =>
        SendCommand($"NAVHTML:{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}");

    public void Show() => SendCommand("SHOW");

    public void Hide() => SendCommand("HIDE");

    /// <summary>
    /// Sends the session token and immediately navigates to the given URL, both in a single pipe
    /// write so the token is guaranteed to be stored before the navigation request fires.
    /// </summary>
    public void SetSessionTokenAndNavigate(string token, string url)
    {
        SendCommands(PipeTokenDeadlineMs, $"TOKEN:{token}", $"NAVIGATE:{url}");
        App.Log.LogInfo($"[Overlay] Session token queued, navigating to {url}.");
    }

    /// <summary>
    /// Posts a typed message to the page via <c>window.postMessage</c>. The page can listen with
    /// <c>onGameMessage</c> from <c>$lib/gameEvents</c>.
    /// </summary>
    public void PostMessage(string type, string? jsonPayload = null)
    {
        // Escape only the characters that would break the JSON string literal.
        var typeEscaped = type.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var json = jsonPayload != null
            ? $"{{\"source\":\"StarTruckerMP\",\"type\":\"{typeEscaped}\",\"payload\":{jsonPayload}}}"
            : $"{{\"source\":\"StarTruckerMP\",\"type\":\"{typeEscaped}\"}}";

        SendCommand($"POSTMESSAGE:{Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}");
    }

    public void PostMessage<T>(string type, T? data)
    {
        var payload = new
        {
            type = type.Replace("\\", "\\\\").Replace("\"", "\\\""),
            source = "StarTruckerMP",
            payload = data
        };
        var json = JsonSerializer.Serialize(payload, OverlayManager.MessageJsonOptions);

        SendCommand($"POSTMESSAGE:{Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}");
    }

    private void SendCommand(string command) => SendCommands(PipeSendDeadlineMs, command);

    private void SendCommands(params string[] commands) => SendCommands(PipeSendDeadlineMs, commands);

    private void SendCommands(int deadlineMs, params string[] commands)
    {
        var pipeName = _pipeName;

        Plugin.StartAttachedThread(() =>
        {
            var deadline = Environment.TickCount64 + deadlineMs;
            var attempts = 0;

            while (true)
            {
                if (Plugin.IsShuttingDown)
                    return;

                attempts++;
                try
                {
                    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    pipe.Connect(timeout: PipeConnectTimeoutMs);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8, bufferSize: 1024, leaveOpen: false);
                    writer.AutoFlush = true;
                    foreach (var cmd in commands)
                        writer.WriteLine(cmd);

                    if (attempts > 1)
                        App.Log.LogInfo($"[Overlay] IPC delivered to {pipeName} after {attempts} attempts.");

                    return;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
                {
                    if (Environment.TickCount64 >= deadline)
                    {
                        App.Log.LogWarning($"[Overlay] IPC send to {pipeName} failed after {attempts} attempts: {ex.Message}");
                        return;
                    }

                    Thread.Sleep(250);
                }
                catch (Exception ex)
                {
                    if (!Plugin.IsShuttingDown)
                        App.Log.LogWarning($"[Overlay] IPC send to {pipeName} failed: {ex.Message}");
                    return;
                }
            }
        });
    }
}

internal static class OverlayManager
{
    #region Win32

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    #endregion

    /// <summary>The instance that draws the in-game page and the F2 panel.</summary>
    private const string HudPipeName = "StarTruckerMP.Overlay";

    /// <summary>The instance that draws the Multiplayer server browser.</summary>
    private const string PanelPipeName = "StarTruckerMP.Overlay.Panel";

    private const string GamePipeName = "StarTruckerMP.Client";

    private static readonly OverlayHandle HudHandle = new(HudPipeName, "hud");

    private static CancellationTokenSource? _gamePipeCts;

    /// <summary>The Multiplayer panel overlay, isolated from the HUD overlay.</summary>
    public static OverlayHandle Panel { get; } = new(PanelPipeName, "panel");

    internal static readonly JsonSerializerOptions MessageJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region Events

    /// <summary>
    /// Raised on a background thread whenever an overlay page calls
    /// <c>sendToGame(type, payload)</c> from JavaScript.
    /// </summary>
    /// <remarks>
    /// <c>type</c>    - the event type string set in JavaScript.<br/>
    /// <c>payload</c> - raw JSON of the payload, or <c>null</c> if omitted.
    /// </remarks>
    public static event Action<string, string?>? MessageReceived;

    #endregion

    #region Public API

    /// <summary>Locate, launch and connect both overlay processes.</summary>
    public static void Launch()
    {
        // Start the game pipe server before the overlay processes so it is ready to accept
        // connections as soon as they boot. Both processes connect to it.
        StartGamePipeServer();

        HudHandle.Launch();
        Panel.Launch();
    }

    /// <summary>Kill both overlay processes (called on plugin unload if needed).</summary>
    public static void Shutdown()
    {
        _gamePipeCts?.Cancel();
        HudHandle.Shutdown();
        Panel.Shutdown();
    }

    // The static API controls the HUD instance; the panel is reached through Panel.

    public static void RunDiagnostics() => HudHandle.RunDiagnostics();

    /// <summary>Navigates the HUD browser to a URL.</summary>
    public static void Navigate(string url) => HudHandle.Navigate(url);

    /// <summary>Loads raw HTML into the HUD browser.</summary>
    public static void NavigateToHtml(string html) => HudHandle.NavigateToHtml(html);

    /// <summary>Shows the HUD overlay.</summary>
    public static void Show() => HudHandle.Show();

    /// <summary>Hides the HUD overlay while keeping the window alive.</summary>
    public static void Hide() => HudHandle.Hide();

    /// <summary>Sends the session token to the HUD overlay and navigates to the given URL.</summary>
    public static void SetSessionTokenAndNavigate(string token, string url) =>
        HudHandle.SetSessionTokenAndNavigate(token, url);

    /// <summary>Posts a typed message to the HUD page.</summary>
    public static void PostMessage(string type, string? jsonPayload = null) =>
        HudHandle.PostMessage(type, jsonPayload);

    public static void PostMessage<T>(string type, T? data) => HudHandle.PostMessage(type, data);

    #endregion

    #region Process start

    /// <summary>Starts one overlay process for the given pipe. Runs on a background thread.</summary>
    internal static Process? StartOverlayProcess(string pipeName, string tag)
    {
        var exe = ResolveOverlayExe();
        if (exe == null)
        {
            App.Log.LogWarning($"[Overlay] StarTruckerMP.Overlay.exe not found ({tag}). Searched:");
            App.Log.LogWarning($"[Overlay]   {Path.Combine(Path.GetDirectoryName(typeof(OverlayManager).Assembly.Location) ?? "", "StarTruckerMP.Overlay.exe")}");
            App.Log.LogWarning($"[Overlay]   {Path.Combine(Path.GetDirectoryName(typeof(OverlayManager).Assembly.Location) ?? "", "overlay", "StarTruckerMP.Overlay.exe")}");
            return null;
        }

        App.Log.LogInfo($"[Overlay] Found exe for {tag} at: {exe}");

        var hwnd = WaitForGameWindow(timeoutMs: 30_000);
        var pid = Process.GetCurrentProcess().Id;
        var ignoreSslValidation = App.IgnoreSslValidation.Value;
        App.Log.LogInfo($"[Overlay] {tag}: Game HWND={hwnd}, PID={pid}, Pipe={pipeName}, IgnoreSslValidation={ignoreSslValidation}");

        // The player id makes the overlay track the game window and exit with the game. The pipe
        // name keeps the two instances apart.
        var psi = new ProcessStartInfo(
            exe,
            $"{hwnd.ToInt64()} {pid} --ignore-certificate-errors={(ignoreSslValidation ? "1" : "0")} --pipe={pipeName}")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };

        try
        {
            var process = Process.Start(psi);
            if (process == null)
            {
                App.Log.LogError($"[Overlay] Process.Start returned null for {tag} - the OS refused to start the process.");
                return null;
            }

            App.Log.LogInfo($"[Overlay] {tag} process started (pid={process.Id}).");

            // Pipe stdout/stderr from the overlay to the BepInEx log.
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    App.Log.LogInfo($"[Overlay:{tag}:out] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data) && !IsChromiumInternalNoise(e.Data))
                    App.Log.LogWarning($"[Overlay:{tag}:err] {e.Data}");
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Detect early crash.
            process.Exited += (_, _) =>
                App.Log.LogWarning($"[Overlay] {tag} process exited with code {process.ExitCode}.");
            process.EnableRaisingEvents = true;

            return process;
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Overlay] Failed to start the {tag} process: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Chromium logs failures of its own push and model services on stderr. The overlay only
    /// renders a local page, so those lines say nothing about the mod and are not worth a warning.
    /// </summary>
    private static bool IsChromiumInternalNoise(string line) =>
        line.Contains("google_apis", StringComparison.OrdinalIgnoreCase)
        || line.Contains("on_device_model", StringComparison.OrdinalIgnoreCase)
        || line.Contains("gcm", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Game window detection

    /// <summary>
    /// Enumerates top-level windows of the current process, skipping the BepInEx
    /// console window, and returns the first visible one (the Unity game window).
    /// </summary>
    private static IntPtr FindGameWindow()
    {
        var currentPid = (uint)Process.GetCurrentProcess().Id;
        var consoleHwnd = GetConsoleWindow();
        var found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (hwnd == consoleHwnd) return true; // skip BepInEx console
            if (!IsWindowVisible(hwnd)) return true; // skip hidden windows

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != currentPid) return true; // skip other processes

            found = hwnd;
            return false; // stop - first match is the game window
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>Polls <see cref="FindGameWindow"/> until it returns a valid handle or the timeout expires.</summary>
    private static IntPtr WaitForGameWindow(int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;

        while (Environment.TickCount < deadline)
        {
            var hwnd = FindGameWindow();
            if (hwnd != IntPtr.Zero) return hwnd;
            Thread.Sleep(250);
        }

        return FindGameWindow(); // last resort
    }

    #endregion

    #region Helpers

    private static string? ResolveOverlayExe()
    {
        var pluginDir = Path.GetDirectoryName(typeof(OverlayManager).Assembly.Location);
        if (string.IsNullOrWhiteSpace(pluginDir)) return null;

        var candidate = Path.Combine(pluginDir, "overlay", "StarTruckerMP.Overlay.exe");
        if (IsOverlayDeploymentValid(candidate)) return candidate;

        candidate = Path.Combine(pluginDir, "StarTruckerMP.Overlay.exe");
        if (IsOverlayDeploymentValid(candidate)) return candidate;

        return null;
    }

    private static bool IsOverlayDeploymentValid(string exePath)
    {
        if (!File.Exists(exePath))
            return false;

        var directory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        string[] requiredFiles =
        [
            "StarTruckerMP.Overlay.deps.json",
            "StarTruckerMP.Overlay.runtimeconfig.json",
            "CefSharp.Core.Runtime.dll",
            "CefSharp.Core.dll",
            "CefSharp.dll",
            "CefSharp.OffScreen.dll",
            "CefSharp.BrowserSubprocess.exe",
            "libcef.dll"
        ];

        var missing = requiredFiles
            .Where(file => !File.Exists(Path.Combine(directory, file)))
            .ToArray();

        if (missing.Length == 0)
            return true;

        App.Log.LogWarning($"[Overlay] Ignoring incomplete deployment at: {exePath}");
        foreach (var file in missing)
            App.Log.LogWarning($"[Overlay]   Missing: {Path.Combine(directory, file)}");

        return false;
    }

    #endregion

    #region Game pipe server (Overlay -> Plugin)

    private static void StartGamePipeServer()
    {
        _gamePipeCts = new CancellationTokenSource();
        var ct = _gamePipeCts.Token;

        Plugin.StartAttachedThread(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var pipe = new NamedPipeServerStream(
                        GamePipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    // Wait for the next overlay to connect; respect cancellation.
                    pipe.WaitForConnectionAsync(ct).Wait(ct);

                    // Each overlay process keeps its connection open for its whole life, so the
                    // server hands it to a reader and immediately listens for the next one.
                    var connection = pipe;
                    Plugin.StartAttachedThread(() => ReadGamePipe(connection, ct));
                }
                catch (OperationCanceledException) { break; }
                catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // While the game closes the pipe is torn down under this thread and the wake
                    // up fails with an SEHException. Logging then calls back into il2cpp and
                    // freezes the exit, so the loop just ends instead.
                    if (Plugin.IsShuttingDown || ct.IsCancellationRequested)
                        break;

                    App.Log.LogWarning($"[Overlay] Game pipe error: {ex.Message}");
                    if (ct.WaitHandle.WaitOne(500))
                        break;
                }
            }
        });
    }

    private static void ReadGamePipe(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, Encoding.UTF8))
            {
                string? line;
                while (!ct.IsCancellationRequested && (line = reader.ReadLine()) != null)
                    DispatchGameMessage(line.Trim());
            }
        }
        catch (Exception ex)
        {
            if (Plugin.IsShuttingDown || ct.IsCancellationRequested)
                return;

            App.Log.LogWarning($"[Overlay] Game pipe reader error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a JSON line received from an overlay and raises
    /// <see cref="MessageReceived"/> with the extracted type and payload.
    /// Expected format: <c>{"type":"eventName","payload":{...}}</c>
    /// </summary>
    private static void DispatchGameMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString() ?? string.Empty;
            var payload = root.TryGetProperty("payload", out var p) ? p.GetRawText() : null;
            MessageReceived?.Invoke(type, payload);
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[Overlay] Failed to parse game message: {ex.Message}");
        }
    }

    #endregion
}
