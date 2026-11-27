#nullable enable
using System;
using System.Collections.Generic;
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

    /// <summary>How many undelivered commands may wait before the oldest ones are dropped.</summary>
    private const int MaxQueuedCommands = 32;

    /// <summary>How long an overlay that is only just starting up may take to open its pipe.</summary>
    private const int PipeLaunchDeadlineMs = 60_000;

    /// <summary>A launch that failed is not repeated faster than this.</summary>
    private const int RelaunchCooldownMs = 10_000;

    /// <summary>
    /// The session token has to survive the whole overlay cold start: on this machine the pipe
    /// server appeared around twenty seconds after the process started.
    /// </summary>
    private const int PipeTokenDeadlineMs = 120_000;

    private readonly string _pipeName;
    private readonly string _tag;
    private readonly object _pageLock = new();
    private readonly object _queueLock = new();
    private readonly object _launchLock = new();
    private readonly List<PendingCommand> _queue = [];
    private Process? _process;
    private bool _launching;
    private long _nextLaunchTick;
    private bool _senderRunning;
    private bool _reportedDead;

    /// <summary>The page this instance was last told to show, so a repeated show does not reload it.</summary>
    private string? _pageUrl;

    public OverlayHandle(string pipeName, string tag)
    {
        _pipeName = pipeName;
        _tag = tag;
    }

    public string PipeName => _pipeName;

    /// <summary>Starts this overlay process; a call while it is already running does nothing.</summary>
    public void Launch() => EnsureRunning();

    /// <summary>
    /// Starts the overlay process when it is not running and reports whether a new one was
    /// started. The game owns both overlays for its whole life, so an instance that exited is
    /// brought back instead of leaving F2 and the server browser with a pipe nobody listens to.
    /// </summary>
    internal bool EnsureRunning()
    {
        lock (_launchLock)
        {
            if (_launching || !HasExited())
                return false;

            if (Environment.TickCount64 < _nextLaunchTick)
                return false;

            _launching = true;
            _nextLaunchTick = Environment.TickCount64 + RelaunchCooldownMs;
            _reportedDead = false;
        }

        App.Log.LogWarning($"[Overlay] Starting the {_tag} overlay process.");

        Plugin.StartAttachedThread(() =>
        {
            var process = OverlayManager.StartOverlayProcess(_pipeName, _tag);

            lock (_launchLock)
            {
                _process = process;
                _launching = false;
            }
        });

        return true;
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

    /// <summary>One pipe write that is still waiting for the overlay to accept a connection.</summary>
    private sealed class PendingCommand(string[] commands, long deadline)
    {
        public string[] Commands { get; set; } = commands;

        public long Deadline { get; set; } = deadline;

        public int Attempts { get; set; }
    }

    /// <summary>Switches this overlay between click-through and input capture.</summary>
    public void SetInteractiveMode(bool enabled) => SendCommand($"INTERACTIVE:{(enabled ? "1" : "0")}");

    public void RunDiagnostics()
    {
        ForgetPage();
        SendCommand("RUNTESTS");
    }

    public void Navigate(string url)
    {
        ForgetPage();
        SendCommand($"NAVIGATE:{url}");
    }

    /// <summary>Loads raw HTML into the overlay.</summary>
    public void NavigateToHtml(string html)
    {
        ForgetPage();
        SendCommand($"NAVHTML:{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}");
    }

    /// <summary>
    /// Shows a page and, only the first time, navigates to it. The overlay hides itself until the
    /// new document is ready, so showing the same page again is an instant unhide without a reload.
    /// </summary>
    public void ShowPage(string url, bool interactive = false)
    {
        bool navigate;
        lock (_pageLock)
        {
            navigate = !string.Equals(_pageUrl, url, StringComparison.Ordinal);
            _pageUrl = url;
        }

        if (navigate && interactive)
        {
            // The game holds the foreground right when the panel is opened in its menu, so it can
            // lend it to the overlay. The overlay then takes the keyboard by itself instead of
            // attaching to the game's input queue, which used to hang the game and its console.
            AllowForeground();
            SendCommands($"NAVIGATE:{url}", "INTERACTIVE:1");
        }
        else if (navigate)
            SendCommand($"NAVIGATE:{url}");
        else if (interactive)
            SendCommands("SHOW", "INTERACTIVE:1");
        else
            SendCommand("SHOW");
    }

    private void ForgetPage()
    {
        lock (_pageLock)
            _pageUrl = null;
    }

    /// <summary>Takes the page a restarted process has to load again, or null when none was set.</summary>
    private string? TakePageForRestart()
    {
        lock (_pageLock)
        {
            var url = _pageUrl;
            _pageUrl = null;
            return url;
        }
    }

    public void Show() => SendCommand("SHOW");

    public void Hide() => SendCommand("HIDE");

    /// <summary>
    /// Stores the session token in this overlay process. Nothing is loaded or shown here: the HUD
    /// window appears only when the player asks for it, never on its own while the game boots.
    /// </summary>
    public void SetSessionToken(string token)
    {
        SendCommands(PipeTokenDeadlineMs, $"TOKEN:{token}");
        App.Log.LogInfo("[Overlay] Session token queued.");
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
        var startSender = false;

        lock (_queueLock)
        {
            // A key pressed over and over must not leave a backlog behind when the overlay is slow
            // or gone, so the oldest commands give way instead of the queue growing without end.
            if (_queue.Count >= MaxQueuedCommands)
                _queue.RemoveAt(0);

            _queue.Add(new PendingCommand(commands, Environment.TickCount64 + deadlineMs));

            if (!_senderRunning)
            {
                _senderRunning = true;
                startSender = true;
            }
        }

        if (startSender && !Plugin.IsShuttingDown)
            Plugin.StartAttachedThread(SendQueuedCommands);
    }

    /// <summary>
    /// The only thread this overlay writes the pipe with. Every command used to start a thread of
    /// its own that then waited up to twenty seconds in a pipe connect, and a pile of threads
    /// attached to the game's runtime is what froze it together with its console.
    /// </summary>
    private void SendQueuedCommands()
    {
        try
        {
            while (true)
            {
                PendingCommand command;

                lock (_queueLock)
                {
                    if (_queue.Count == 0)
                        return;

                    command = _queue[0];
                }

                if (Plugin.IsShuttingDown)
                    return;

                // The overlay process may have exited while the game kept running. Starting it
                // again here is what makes F2 work after that instead of talking to a dead pipe.
                if (EnsureRunning())
                {
                    var page = TakePageForRestart();
                    if (page != null && Array.IndexOf(command.Commands, "HIDE") < 0)
                        command.Commands = [$"NAVIGATE:{page}", .. command.Commands];

                    command.Deadline = Environment.TickCount64 + PipeLaunchDeadlineMs;
                }

                command.Attempts++;

                string? failure;
                try
                {
                    using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                    pipe.Connect(timeout: PipeConnectTimeoutMs);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8, bufferSize: 1024, leaveOpen: false);
                    writer.AutoFlush = true;
                    foreach (var line in command.Commands)
                        writer.WriteLine(line);

                    lock (_queueLock)
                        _queue.Remove(command);

                    if (command.Attempts > 1)
                        App.Log.LogInfo($"[Overlay] IPC delivered to {_pipeName} after {command.Attempts} attempts.");

                    continue;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
                {
                    failure = ex.Message;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                    if (!Plugin.IsShuttingDown)
                        App.Log.LogWarning($"[Overlay] IPC send to {_pipeName} failed: {ex.Message}");
                    return;
                }

                // An overlay that is only just starting up has no pipe yet, so the command waits
                // for it until its deadline instead of being reported as a failure.
                if (HasExited())
                {
                    if (Environment.TickCount64 < command.Deadline)
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    lock (_queueLock)
                        _queue.Remove(command);

                    ReportDeadOnce();
                    continue;
                }

                if (Environment.TickCount64 >= command.Deadline)
                {
                    lock (_queueLock)
                        _queue.Remove(command);

                    App.Log.LogWarning($"[Overlay] IPC send to {_pipeName} failed after {command.Attempts} attempts: {failure}");
                    continue;
                }

                Thread.Sleep(250);
            }
        }
        finally
        {
            // A sender that ends, whether it delivered everything or not, hands the queue back so
            // the next command can start a fresh one.
            lock (_queueLock)
            {
                _queue.Clear();
                _senderRunning = false;
            }
        }
    }

    /// <summary>True when no overlay process is running: one that is still booting counts as alive.</summary>
    private bool HasExited()
    {
        lock (_launchLock)
        {
            if (_launching)
                return false;

            if (_process is null)
                return true;

            try
            {
                return _process.HasExited;
            }
            catch
            {
                // A process object that cannot answer is one that is not there any more.
                return true;
            }
        }
    }

    private void ReportDeadOnce()
    {
        if (_reportedDead)
            return;

        _reportedDead = true;
        App.Log.LogWarning($"[Overlay] The {_tag} overlay is not running; its commands are dropped until the next request.");
    }

    private void AllowForeground()
    {
        try
        {
            // ASFW_ANY: every process may take the foreground from this one, once.
            AllowSetForegroundWindow(unchecked((int)0xFFFFFFFF));
        }
        catch
        {
            // Without the permission the panel still opens; the keyboard may need one click.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
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

    /// <summary>Shows the HUD page, navigating only when it is not the page already loaded.</summary>
    public static void ShowPage(string url) => HudHandle.ShowPage(url);

    /// <summary>Hides the HUD overlay while keeping the window alive.</summary>
    public static void Hide() => HudHandle.Hide();

    /// <summary>Sends the session token to the HUD overlay without loading or showing a page.</summary>
    public static void SetSessionToken(string token) => HudHandle.SetSessionToken(token);

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
