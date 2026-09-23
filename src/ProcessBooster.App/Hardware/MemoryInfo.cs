using System.Management;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects detailed physical RAM inventory via WMI/CIM: total installed, slot usage, max
/// capacity, and a per-module breakdown (capacity, speed, type, form factor, vendor, slot).
/// Every query is defensive — a missing class or property yields "—" rather than throwing, so
/// <see cref="Collect"/> always returns and partial data still renders.
/// </summary>
public static class MemoryInfoService
{
    public static List<InfoSection> Collect()
    {
        try
        {
            var cs = First("Win32_ComputerSystem");
            var array = First("Win32_PhysicalMemoryArray");
            var modules = All("Win32_PhysicalMemory");

            var sections = new List<InfoSection> { OverviewSection(cs, array, modules) };
            sections.AddRange(ModuleSections(modules));
            return sections;
        }
        catch
        {
            // Collect() must never throw — surface a minimal placeholder instead.
            var s = new InfoSection { Title = "Memory" };
            s.Items.Add(new("Status", "—"));
            return new List<InfoSection> { s };
        }
    }

    private static InfoSection OverviewSection(
        ManagementBaseObject? cs, ManagementBaseObject? array, List<ManagementBaseObject> modules)
    {
        var s = new InfoSection { Title = "Overview" };

        // Total installed — prefer the OS-reported figure, fall back to summing modules.
        if (ulong.TryParse(Str(cs, "TotalPhysicalMemory"), out var total) && total > 0)
            s.Items.Add(new("Total installed", Gb(total)));
        else
        {
            ulong sum = 0;
            foreach (var m in modules)
                if (ulong.TryParse(Str(m, "Capacity"), out var c)) sum += c;
            s.Items.Add(new("Total installed", sum > 0 ? Gb(sum) : "—"));
        }

        // Slots used / total.
        var used = 0;
        foreach (var m in modules)
            if (ulong.TryParse(Str(m, "Capacity"), out var c) && c > 0) used++;
        var totalSlots = Str(array, "MemoryDevices");
        s.Items.Add(new("Slots used", totalSlots is not "—"
            ? $"{used} used  ·  {totalSlots} total"
            : $"{used} used"));

        // Max capacity — MaxCapacity is in KB.
        if (ulong.TryParse(Str(array, "MaxCapacity"), out var maxKb) && maxKb > 0)
            s.Items.Add(new("Max capacity", Gb(maxKb * 1024UL)));
        else
            s.Items.Add(new("Max capacity", "—"));

        // Memory type & speed — read from the first populated module (uniform in practice).
        var firstModule = modules.FirstOrDefault(
            m => ulong.TryParse(Str(m, "Capacity"), out var c) && c > 0) ?? modules.FirstOrDefault();
        s.Items.Add(new("Memory type", MemoryTypeOf(firstModule)));

        var speed = Str(firstModule, "Speed");
        var configured = Str(firstModule, "ConfiguredClockSpeed");
        s.Items.Add(new("Speed", SpeedText(speed, configured)));

        return s;
    }

    /// <summary>One clean card per populated module, with short label/value rows that don't wrap.</summary>
    private static IEnumerable<InfoSection> ModuleSections(List<ManagementBaseObject> modules)
    {
        var populated = modules
            .Where(m => ulong.TryParse(Str(m, "Capacity"), out var c) && c > 0)
            .ToList();
        if (populated.Count == 0)
            return new[] { new InfoSection { Title = "Modules", Items = { new("Modules", "—") } } };

        var list = new List<InfoSection>();
        var i = 1;
        foreach (var m in populated)
        {
            var s = new InfoSection { Title = $"Module {i++}" };

            if (ulong.TryParse(Str(m, "Capacity"), out var c) && c > 0) s.Items.Add(new("Capacity", Gb(c)));
            s.Items.Add(new("Type", MemoryTypeOf(m)));
            s.Items.Add(new("Form factor", FormFactorOf(m)));

            var speed = Str(m, "Speed");
            if (speed is not "—") s.Items.Add(new("Speed", $"{speed} MT/s"));
            var configured = Str(m, "ConfiguredClockSpeed");
            if (configured is not "—" && configured != speed) s.Items.Add(new("Configured", $"{configured} MT/s"));

            AddIf(s, "Manufacturer", Str(m, "Manufacturer"));
            AddIf(s, "Part number", Str(m, "PartNumber"));
            AddIf(s, "Slot", Str(m, "DeviceLocator"));
            AddIf(s, "Bank", Str(m, "BankLabel"));

            var dataW = Str(m, "DataWidth");
            var totalW = Str(m, "TotalWidth");
            if (dataW is not "—")
                s.Items.Add(new("Width", totalW is not "—" ? $"{dataW}/{totalW}-bit" : $"{dataW}-bit"));

            var volts = MilliVolts(Str(m, "ConfiguredVoltage"));
            if (volts is not "—") s.Items.Add(new("Voltage", volts));

            list.Add(s);
        }
        return list;
    }

    private static void AddIf(InfoSection s, string label, string value)
    {
        if (value is not "—") s.Items.Add(new(label, value));
    }

    // ---- WMI helpers ----
    private static List<ManagementBaseObject> All(string wmiClass)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {wmiClass}");
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch { return new(); }
    }

    private static ManagementBaseObject? First(string wmiClass) => All(wmiClass).FirstOrDefault();

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    /// <summary>Reads a numeric property, returning null on any failure.</summary>
    private static ushort? U16(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop] is { } o && ushort.TryParse(o.ToString(), out var v) ? v : null; }
        catch { return null; }
    }

    // ---- Formatting ----
    private static string Gb(ulong bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.#} GB";

    private static string SpeedText(string speed, string configured)
    {
        if (speed is "—" && configured is "—") return "—";
        if (configured is not "—" && configured != speed && speed is not "—")
            return $"{speed} MT/s (running {configured} MT/s)";
        return $"{(speed is not "—" ? speed : configured)} MT/s";
    }

    private static string MilliVolts(string mv)
        => int.TryParse(mv, out var v) && v > 0 ? $"{v / 1000.0:0.##} V" : "—";

    /// <summary>Decodes Win32_PhysicalMemory.FormFactor (SMBIOS enum).</summary>
    private static string FormFactorOf(ManagementBaseObject? m) => U16(m, "FormFactor") switch
    {
        8 => "DIMM",
        12 => "SODIMM",
        11 => "RIMM",
        13 => "SRIMM",
        null => "—",
        _ => "Other",
    };

    /// <summary>
    /// Decodes the memory technology. Prefers SMBIOSMemoryType (raw SMBIOS byte, most reliable on
    /// modern systems) and falls back to the legacy MemoryType enum.
    /// </summary>
    private static string MemoryTypeOf(ManagementBaseObject? m)
    {
        // SMBIOS memory type (SMBIOS spec, Memory Device "Type" field).
        var smbios = U16(m, "SMBIOSMemoryType");
        var fromSmbios = smbios switch
        {
            0x12 => "DDR",     // 18
            0x13 => "DDR2",    // 19
            0x14 => "DDR2 FB-DIMM", // 20
            0x18 => "DDR3",    // 24
            0x1A => "DDR4",    // 26
            0x1B => "LPDDR",   // 27
            0x1C => "LPDDR2",  // 28
            0x1D => "LPDDR3",  // 29
            0x1E => "LPDDR4",  // 30
            0x22 => "DDR5",    // 34
            0x23 => "LPDDR5",  // 35
            _ => null,
        };
        if (fromSmbios is not null) return fromSmbios;

        // Legacy Win32_PhysicalMemory.MemoryType enum.
        var legacy = U16(m, "MemoryType") switch
        {
            20 => "DDR",
            21 => "DDR2",
            22 => "DDR2 FB-DIMM",
            24 => "DDR3",
            26 => "DDR4",
            27 => "LPDDR",
            28 => "LPDDR2",
            29 => "LPDDR3",
            30 => "LPDDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            _ => null,
        };
        return legacy ?? "—";
    }
}
