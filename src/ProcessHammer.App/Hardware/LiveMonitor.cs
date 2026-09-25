using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// A single shared source of live readings for every tab: one LibreHardwareMonitor instance plus
/// network throughput, polled on one timer. Tabs subscribe to <see cref="Updated"/> and read the
/// cached values, so we never spin up multiple sensor drivers or duplicate polling.
/// </summary>
public sealed class LiveMonitor : IDisposable
{
    private readonly SensorService _sensors = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;

    private long _prevRx, _prevTx;
    private DateTime _prevAt = DateTime.UtcNow;

    public SensorSnapshot Sensors { get; private set; } = new();
    public IReadOnlyList<SensorGroup> SensorGroups => _sensors.AllGroups;
    public string? SensorNote => _sensors.IsAvailable ? null : _sensors.Unavailable;

    public double NetDownMbps { get; private set; }
    public double NetUpMbps { get; private set; }

    /// <summary>Raised on the UI thread after each poll.</summary>
    public event Action? Updated;

    private int _consumers;

    public LiveMonitor()
    {
        (_prevRx, _prevTx) = ReadNetTotals();
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>A visible live tab calls this; the sensor timer only runs while at least one is active.</summary>
    public void AddConsumer()
    {
        if (++_consumers != 1) return;
        (_prevRx, _prevTx) = ReadNetTotals();  // reset baseline so throughput isn't a resume spike
        _prevAt = DateTime.UtcNow;
        _timer.Start();
        Poll();
    }

    public void RemoveConsumer()
    {
        if (_consumers > 0 && --_consumers == 0) _timer.Stop();
    }

    private async void Poll()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_sensors.IsAvailable)
                Sensors = await Task.Run(_sensors.Poll);

            UpdateNet();
            Updated?.Invoke();
        }
        finally { _busy = false; }
    }

    private void UpdateNet()
    {
        var (rx, tx) = ReadNetTotals();
        var now = DateTime.UtcNow;
        var elapsed = Math.Max(0.001, (now - _prevAt).TotalSeconds);
        NetDownMbps = Math.Max(0, (rx - _prevRx) * 8 / 1_000_000.0 / elapsed);
        NetUpMbps = Math.Max(0, (tx - _prevTx) * 8 / 1_000_000.0 / elapsed);
        _prevRx = rx; _prevTx = tx; _prevAt = now;
    }

    private static (long Rx, long Tx) ReadNetTotals()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPv4Statistics();
                rx += s.BytesReceived; tx += s.BytesSent;
            }
        }
        catch { }
        return (rx, tx);
    }

    public void Dispose()
    {
        _timer.Stop();
        _sensors.Dispose();
    }
}
