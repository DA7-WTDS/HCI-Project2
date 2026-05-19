using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AnimalHomeGame_CSharp;

/// <summary>
/// Receives plain-text TUIO commands forwarded by tuio_bridge.py on port 3334.
///
/// Message format (one per UDP datagram):
///   TUIO_ADD:symbolId,x,y   — marker appeared
///   TUIO_UPD:symbolId,x,y   — marker moved
///   TUIO_REM:symbolId,x,y   — marker removed
///
/// This replaces TCD.System.TUIO which silently fails on .NET 10.
/// </summary>
public class TuioHandler : IDisposable
{
    private const int ListenPort = 3334;

    private UdpClient?  _udp;
    private Thread?     _thread;
    private bool        _running;

    public Action<int, float, float>? OnObjectAdded;
    public Action<int, float, float>? OnObjectUpdated;
    public Action<int, float, float>? OnObjectRemoved;

    public void Start(int port = ListenPort)
    {
        try
        {
            _udp = new UdpClient(port);
            _running = true;
            _thread  = new Thread(Listen) { IsBackground = true, Name = "TuioListener" };
            _thread.Start();
            Console.WriteLine($"[TuioHandler] Listening on UDP port {port}.");
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(
                $"Could not start TUIO listener on port {port}.\n{ex.Message}\n\n" +
                "Make sure tuio_bridge.py is running.",
                "TUIO Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
    }

    public void Stop()
    {
        _running = false;
        try { _udp?.Close(); } catch { }
        _udp = null;
    }

    public void Dispose() => Stop();

    // ── Background listener thread ─────────────────────────────────────────
    private void Listen()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[]? data = _udp?.Receive(ref ep);
                if (data == null) continue;

                string msg = Encoding.UTF8.GetString(data).Trim();
                ParseAndDispatch(msg);
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)         { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[TuioHandler] Error: {ex.Message}");
            }
        }
    }

    private void ParseAndDispatch(string msg)
    {
        // Format: TUIO_ADD:id,x,y  or TUIO_UPD:id,x,y  or TUIO_REM:id,x,y
        int colon = msg.IndexOf(':');
        if (colon < 0) return;

        string cmd    = msg[..colon];
        string[] args = msg[(colon + 1)..].Split(',');
        if (args.Length < 3) return;

        if (!int.TryParse(args[0], out int symbolId))          return;
        if (!float.TryParse(args[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float x)) return;
        if (!float.TryParse(args[2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float y)) return;

        switch (cmd)
        {
            case "TUIO_ADD": OnObjectAdded?.Invoke(symbolId, x, y);   break;
            case "TUIO_UPD": OnObjectUpdated?.Invoke(symbolId, x, y); break;
            case "TUIO_REM": OnObjectRemoved?.Invoke(symbolId, x, y); break;
            default:
                Console.WriteLine($"[TuioHandler] Unknown command: {cmd}");
                break;
        }
    }
}
