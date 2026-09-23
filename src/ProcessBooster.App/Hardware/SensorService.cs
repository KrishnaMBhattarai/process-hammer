using LibreHardwareMonitor.Hardware;

namespace ProcessBooster.App.Hardware;

/// <summary>A snapshot of live sensor readings. Any value the platform doesn't expose stays null.</summary>
public sealed record SensorSnapshot
{
    public double? CpuTempC { get; init; }
    public double? CpuLoad { get; init; }
    public double? CpuClockMhz { get; init; }
    public double? CpuPowerW { get; init; }
    public double? GpuTempC { get; init; }
    public double? GpuLoad { get; init; }
    public double? GpuClockMhz { get; init; }
    public double? GpuVramUsedGb { get; init; }
    public double? GpuVramTotalGb { get; init; }
    public double? MemLoad { get; init; }
    public double? MemUsedGb { get; init; }
    public double? FanRpm { get; init; }
    public string? GpuName { get; init; }
}

/// <summary>One sensor reading, already formatted for display.</summary>
public sealed record SensorLine(string Name, string Value);

/// <summary>All sensors belonging to one hardware device.</summary>
public sealed record SensorGroup(string Hardware, IReadOnlyList<SensorLine> Lines);

/// <summary>
/// Live hardware sensors via LibreHardwareMonitor. Reading real CPU/GPU temperatures and clocks
/// needs its kernel sensor driver, which needs admin. If it can't initialize, <see cref="IsAvailable"/>
/// is false and callers show "—" while the rest of the System tab still works.
/// </summary>
public sealed class SensorService : IDisposable
{
    private readonly Computer? _computer;
    private readonly UpdateVisitor _visitor = new();

    public bool IsAvailable { get; }
    public string? Unavailable { get; }

    /// <summary>Every sensor from the last Poll, grouped by hardware device (for the Sensors tab).</summary>
    public IReadOnlyList<SensorGroup> AllGroups { get; private set; } = Array.Empty<SensorGroup>();

    public SensorService()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = false,
                IsControllerEnabled = false,
            };
            _computer.Open();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Unavailable = ex.Message;
        }
    }

    public SensorSnapshot Poll()
    {
        if (_computer is null || !IsAvailable) return new SensorSnapshot();

        _computer.Accept(_visitor);
        var all = new List<(HardwareType Type, ISensor Sensor)>();
        foreach (var hw in _computer.Hardware) Collect(hw, all);

        double? Pick(HardwareType type, SensorType sensor, params string[] nameContains)
        {
            var matches = all.Where(x => x.Type == type && x.Sensor.SensorType == sensor && x.Sensor.Value is not null).ToList();
            if (matches.Count == 0) return null;
            foreach (var n in nameContains)
            {
                var hit = matches.FirstOrDefault(x => x.Sensor.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
                if (hit.Sensor is not null) return hit.Sensor.Value;
            }
            return matches.Max(x => (double)x.Sensor.Value!);
        }

        bool IsGpu(HardwareType t) => t is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        var gpuType = all.Select(x => x.Type).FirstOrDefault(IsGpu, HardwareType.GpuNvidia);
        var gpuName = _computer.Hardware.FirstOrDefault(h => IsGpu(h.HardwareType))?.Name;

        // Average of all per-core clocks — reflects real load and updates each tick, unlike a single
        // core pinned at its max boost (which looked static).
        double? CpuClock()
        {
            var cores = all.Where(x => x.Type == HardwareType.Cpu && x.Sensor.SensorType == SensorType.Clock
                && x.Sensor.Value is not null && x.Sensor.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase)).ToList();
            return cores.Count > 0 ? cores.Average(x => (double)x.Sensor.Value!) : Pick(HardwareType.Cpu, SensorType.Clock, "Core");
        }

        double? gpuVramUsed = Pick(gpuType, SensorType.SmallData, "Memory Used", "GPU Memory Used");
        double? gpuVramTotal = Pick(gpuType, SensorType.SmallData, "Memory Total", "GPU Memory Total");

        AllGroups = BuildGroups();

        return new SensorSnapshot
        {
            CpuTempC = Pick(HardwareType.Cpu, SensorType.Temperature, "Package", "Tctl/Tdie", "Core Max", "Core Average"),
            CpuLoad = Pick(HardwareType.Cpu, SensorType.Load, "CPU Total"),
            CpuClockMhz = CpuClock(),
            CpuPowerW = Pick(HardwareType.Cpu, SensorType.Power, "Package", "CPU Package"),
            GpuTempC = Pick(gpuType, SensorType.Temperature, "GPU Core", "Core"),
            GpuLoad = Pick(gpuType, SensorType.Load, "GPU Core", "Core"),
            GpuClockMhz = Pick(gpuType, SensorType.Clock, "GPU Core", "Core"),
            GpuVramUsedGb = gpuVramUsed is { } u ? u / 1024.0 : null,
            GpuVramTotalGb = gpuVramTotal is { } t ? t / 1024.0 : null,
            MemLoad = Pick(HardwareType.Memory, SensorType.Load, "Memory"),
            MemUsedGb = Pick(HardwareType.Memory, SensorType.Data, "Memory Used"),
            FanRpm = Pick(HardwareType.Motherboard, SensorType.Fan) ?? Pick(HardwareType.SuperIO, SensorType.Fan),
            GpuName = gpuName,
        };
    }

    private static void Collect(IHardware hw, List<(HardwareType, ISensor)> sink)
    {
        foreach (var s in hw.Sensors) sink.Add((hw.HardwareType, s));
        foreach (var sub in hw.SubHardware) Collect(sub, sink);
    }

    private List<SensorGroup> BuildGroups()
    {
        var groups = new List<SensorGroup>();
        if (_computer is null) return groups;
        foreach (var hw in _computer.Hardware) AddGroup(hw, groups);
        return groups;
    }

    private static void AddGroup(IHardware hw, List<SensorGroup> groups)
    {
        var lines = hw.Sensors
            .Where(s => s.Value is not null)
            .OrderBy(s => s.SensorType).ThenBy(s => s.Name)
            .Select(s => new SensorLine(s.Name, FormatSensor(s)))
            .ToList();
        if (lines.Count > 0) groups.Add(new SensorGroup(hw.Name, lines));
        foreach (var sub in hw.SubHardware) AddGroup(sub, groups);
    }

    private static string FormatSensor(ISensor s)
    {
        var v = s.Value ?? 0f;
        return s.SensorType switch
        {
            SensorType.Temperature => $"{v:0.0} °C",
            SensorType.Load => $"{v:0} %",
            SensorType.Clock => $"{v:0} MHz",
            SensorType.Fan => $"{v:0} RPM",
            SensorType.Voltage => $"{v:0.000} V",
            SensorType.Current => $"{v:0.00} A",
            SensorType.Power => $"{v:0.0} W",
            SensorType.Data => $"{v:0.0} GB",
            SensorType.SmallData => $"{v:0} MB",
            SensorType.Throughput => $"{v / 1024.0 / 1024.0:0.0} MB/s",
            SensorType.Frequency => $"{v:0} Hz",
            SensorType.Control => $"{v:0} %",
            SensorType.Level => $"{v:0} %",
            SensorType.Factor => $"{v:0.00}",
            _ => $"{v:0.##}",
        };
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
