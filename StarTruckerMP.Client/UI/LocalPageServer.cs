#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace StarTruckerMP.Client.UI;

/// <summary>
/// A small loopback HTTP server for the pages the plugin shows in the overlay. The overlay browser
/// renders plain http pages reliably, while an embedded or data URL showed its own error page, and a
/// loopback listener avoids the self-signed certificate of the game server.
/// </summary>
internal sealed class LocalPageServer
{
    private readonly string _threadName;
    private readonly object _gate = new();

    private TcpListener? _listener;
    private int _port;
    private volatile string _html = "";
    private string _url = "";
    private int _version;

    public LocalPageServer(string threadName) => _threadName = threadName;

    /// <summary>
    /// Stores the page, starts the listener on the first call and returns its URL. The URL only
    /// changes when the page itself changes, so showing the same page again does not reload it.
    /// </summary>
    public string Publish(string html)
    {
        lock (_gate)
        {
            if (_listener is null)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _listener = listener;

                var thread = new Thread(() => AcceptLoop(listener))
                {
                    IsBackground = true,
                    Name = _threadName
                };
                thread.Start();

                App.Log.LogInfo($"[Overlay] {_threadName} page served at http://127.0.0.1:{_port}/");
            }

            if (_url.Length == 0 || !string.Equals(_html, html, StringComparison.Ordinal))
            {
                _html = html;
                _version++;
                _url = $"http://127.0.0.1:{_port}/?v={_version}";
            }

            return _url;
        }
    }

    private void AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            try
            {
                var client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
            catch (Exception)
            {
                // The listener dies with the game process; there is nothing else to do here.
                return;
            }
        }
    }

    private void Serve(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = reader.ReadLine() ?? string.Empty;
                while (!string.IsNullOrEmpty(reader.ReadLine()))
                {
                    // Skip the request headers.
                }

                var isIcon = requestLine.Contains("/favicon", StringComparison.OrdinalIgnoreCase);
                var body = Encoding.UTF8.GetBytes(isIcon ? "" : _html);
                var status = isIcon ? "404 Not Found" : "200 OK";
                var header = $"HTTP/1.1 {status}\r\n" +
                             "Content-Type: text/html; charset=utf-8\r\n" +
                             "Cache-Control: no-store\r\n" +
                             $"Content-Length: {body.Length}\r\n" +
                             "Connection: close\r\n\r\n";

                var headerBytes = Encoding.ASCII.GetBytes(header);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
        }
        catch (Exception)
        {
            // A closed page must never affect the game.
        }
    }
}
