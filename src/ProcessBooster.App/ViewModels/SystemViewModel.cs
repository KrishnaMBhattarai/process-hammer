using System.Collections.ObjectModel;
using System.Windows.Threading;
using ProcessBooster.App.Hardware;

namespace ProcessBooster.App.ViewModels;

/// <summary>One live dashboard tile (label + big value).</summary>
public sealed class StatTile : ViewModelBase
{
    public string Label { get; }
    private string _value = "—";
    public string Value { get => _value; set => SetField(ref _value, value); }
    public StatTile(string label) => Label = label;
}

/// <summary>Backs the System tab: static inventory (loaded once) + live sensor tiles (polled).</summary>
public sealed class SystemViewModel : ViewModelBase, IDisposable
{
    private readonly SensorService _sensors = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _polling;

    public ObservableCollection<InfoSection> Sections { get; } = new();
    public ObservableCollection<StatTile> Tiles { get; } = new();

    public string SensorStatus { get; }
    public bool SensorsAvailable => _sensors.IsAvailable;

    private readonly StatTile _cpuTemp = new("CPU temp"), _cpuLoad = new("CPU load"),
        _cpuClock = new("CPU clock"), _cpuPower = new("CPU power"), _ram = new("Memory used"),
        _gpuTemp = new("GPU temp"), _gpuLoad = new("GPU load"), _gpuClock = new("GPU clock"),
        _gpuVram = new("GPU memory"), _fan = new("Fan");

    public SystemViewModel()
    {
        foreach (var t in new[] { _cpuTemp, _cpuLoad, _cpuClock, _cpuPower, _ram, _gpuTemp, _gpuLoad, _gpuClock, _gpuVram, _fan })
            Tiles.Add(t);

        SensorStatus = _sensors.IsAvailable
            ? ""
            : $"Live sensors couldn't start ({_sensors.Unavailable}). Static specs are still shown below; live temps/clocks need the sensor driver (admin).";
        _timer.Tick += (_, _) => Poll();
    }

    public async void Start()
    {
        var sections = await Task.Run(SystemInfoService.Collect); // WMI is slow — off-thread
        foreach (var s in sections) Sections.Add(s);
        Poll();
        _timer.Start();
    }

    private async void Poll()
    {
        if (_polling || !_sensors.IsAvailable) return;
        _polling = true;
        try
        {
            var s = await Task.Run(_sensors.Poll);
            _cpuTemp.Value = Fmt(s.CpuTempC, "°C");
            _cpuLoad.Value = Fmt(s.CpuLoad, "%");
            _cpuClock.Value = s.CpuClockMhz is { } c ? $"{c / 1000.0:0.00} GHz" : "—";
            _cpuPower.Value = Fmt(s.CpuPowerW, " W");
            _ram.Value = s.MemUsedGb is { } m ? $"{m:0.0} GB" : "—";
            _gpuTemp.Value = Fmt(s.GpuTempC, "°C");
            _gpuLoad.Value = Fmt(s.GpuLoad, "%");
            _gpuClock.Value = s.GpuClockMhz is { } gc ? $"{gc:0} MHz" : "—";
            _gpuVram.Value = s.GpuVramUsedGb is { } vu
                ? (s.GpuVramTotalGb is { } vt ? $"{vu:0.0}/{vt:0.0} GB" : $"{vu:0.0} GB")
                : "—";
            _fan.Value = s.FanRpm is { } f ? $"{f:0} RPM" : "—";
        }
        finally { _polling = false; }
    }

    private static string Fmt(double? v, string suffix) => v is { } d ? $"{d:0}{suffix}" : "—";

    public void Dispose()
    {
        _timer.Stop();
        _sensors.Dispose();
    }
}
