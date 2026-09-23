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
            sections.Add(ModulesSection(modules));
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

    private static InfoSection ModulesSection(List<ManagementBaseObject> modules)
    {
        var s = new InfoSection { Title = "Modules" };
        if (modules.Count == 0)
        {
            s.Items.Add(new("Modules", "—"));
            return s;
        }

        var i = 1;
        foreach (var m in modules)
        {
            var cap = ulong.TryParse(Str(m, "Capacity"), out var c) && c > 0 ? Gb(c) : "—";
            var speed = Str(m, "Speed");
            var slot = Str(m, "DeviceLocator");
            var label = slot is not "—" ? $"Module {i} ({slot})" : $"Module {i}";
            i++;

            // Primary row: capacity @ speed · type · form factor.
            s.Items.Add(new(label,
                $"{cap} @ {(speed is not "—" ? speed + " MT/s" : "— MT/s")}  ·  " +
                $"{MemoryTypeOf(m)}  ·  {FormFactorOf(m)}"));

            // Detail row: manufacturer · part number · bank.
            var mfr = Str(m, "Manufacturer");
            var part = Str(m, "PartNumber");
            var bank = Str(m, "BankLabel");
            var detail = $"{mfr}  ·  {part}";
            if (bank is not "—") detail += $"  ·  {bank}";
            s.Items.Add(new("  Details", detail));

            // Electrical row: widths · configured clock · voltage · serial.
            var dataW = Str(m, "DataWidth");
            var totalW = Str(m, "TotalWidth");
            var width = dataW is not "—" && totalW is not "—"
                ? $"{dataW}/{totalW}-bit"
                : (dataW is not "—" ? $"{dataW}-bit" : "—");
            var configured = Str(m, "ConfiguredClockSpeed");
            var volts = MilliVolts(Str(m, "ConfiguredVoltage"));
            var serial = Str(m, "SerialNumber");
            s.Items.Add(new("  Electrical",
                $"width {width}  ·  configured {(configured is not "—" ? configured + " MT/s" : "—")}" +
                $"  ·  {volts}  ·  S/N {serial}"));
        }
        return s;
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
