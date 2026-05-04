using System;
using TCD.System.TUIO;

namespace AnimalHomeGame_CSharp;

public class TuioHandler : TuioListener, IDisposable
{
    private TuioClient? client;

    public Action<int, float, float>? OnObjectAdded;
    public Action<int, float, float>? OnObjectUpdated;
    public Action<int, float, float>? OnObjectRemoved;

    public void Start(int port = 3333)
    {
        try
        {
            client = new TuioClient(port);
            client.addTuioListener(this);
            client.connect();
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(
                $"Could not start TUIO listener on port {port}.\n{ex.Message}",
                "TUIO Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
    }

    public void Stop()
    {
        try
        {
            client?.removeTuioListener(this);
            client?.disconnect();
        }
        catch { }
        finally { client = null; }
    }

    public void addTuioObject(TuioObject obj) => OnObjectAdded?.Invoke(obj.getSymbolID(), obj.getX(), obj.getY());
    public void updateTuioObject(TuioObject obj) => OnObjectUpdated?.Invoke(obj.getSymbolID(), obj.getX(), obj.getY());
    public void removeTuioObject(TuioObject obj) => OnObjectRemoved?.Invoke(obj.getSymbolID(), obj.getX(), obj.getY());

    public void addTuioCursor(TuioCursor cursor) { }
    public void updateTuioCursor(TuioCursor cursor) { }
    public void removeTuioCursor(TuioCursor cursor) { }
    public void refresh(TuioTime time) { }

    public void Dispose() => Stop();
}
