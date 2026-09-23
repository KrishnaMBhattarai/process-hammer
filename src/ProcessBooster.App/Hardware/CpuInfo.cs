using System.Management;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects rich per-processor detail via WMI (Win32_Processor). Enumerates every physical
/// processor package (socket) present, labelling them "Socket n/N" only when more than one
/// exists. Every query and property read is defensive: a missing class or property yields "—"
/// and absent items are skipped, so <see cref="Collect"/> never throws and partial data still
/// renders. Reuses <see cref="InfoItem"/> / <see cref="InfoSection"/> from this namespace.
/// </summary>
public static class CpuInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        try
        {
            var cpus = All("Win32_Processor");
            if (cpus.Count == 0)
            {
                var empty = new InfoSection { Title = "Processor" };
                empty.Items.Add(new("Status", "No processor detected"));
                sections.Add(empty);
                return sections;
            }

            var multi = cpus.Count > 1;
            for (var i = 0; i < cpus.Count; i++)
            {
                var prefix = multi ? $"Socket {i + 1}/{cpus.Count} · " : "";
                var cpu = cpus[i];
                sections.Add(ProcessorSection(cpu, prefix));
                sections.Add(CoresSection(cpu, prefix));
                sections.Add(ClocksSection(cpu, prefix));
                sections.Add(CapabilitiesSection(cpu, prefix));
            }
        }
        catch
        {
            // Collect() must never throw — return whatever we gathered.
        }
        return sections;
    }

    private static InfoSection ProcessorSection(ManagementBaseObject cpu, string prefix)
    {
        var s = new InfoSection { Title = $"{prefix}Processor" };
        Add(s, "Model", Str(cpu, "Name"));
        Add(s, "Manufacturer", Str(cpu, "Manufacturer"));
        Add(s, "Description", Str(cpu, "Description"));
        Add(s, "Architecture", Architecture(Str(cpu, "Architecture")));
        Add(s, "Socket", Str(cpu, "SocketDesignation"));
        Add(s, "Processor ID", Str(cpu, "ProcessorId"));
        Add(s, "Family / model / stepping", FamilyLine(cpu));
        return s;
    }

    private static InfoSection CoresSection(ManagementBaseObject cpu, string prefix)
    {
        var s = new InfoSection { Title = $"{prefix}Cores & threads" };
        Add(s, "Physical cores", Str(cpu, "NumberOfCores"));
        Add(s, "Logical processors", Str(cpu, "NumberOfLogicalProcessors"));
        Add(s, "Thread count", Str(cpu, "ThreadCount"));
        return s;
    }

    private static InfoSection ClocksSection(ManagementBaseObject cpu, string prefix)
    {
        var s = new InfoSection { Title = $"{prefix}Clocks & cache" };
        Add(s, "Base clock", Mhz(Str(cpu, "MaxClockSpeed")));
        Add(s, "Current clock", Mhz(Str(cpu, "CurrentClockSpeed")));
        Add(s, "L2 cache", Kb(Str(cpu, "L2CacheSize")));
        Add(s, "L3 cache", Kb(Str(cpu, "L3CacheSize")));
        return s;
    }

    private static InfoSection CapabilitiesSection(ManagementBaseObject cpu, string prefix)
    {
        var s = new InfoSection { Title = $"{prefix}Capabilities" };
        Add(s, "Data / address width", WidthLine(cpu));
        Add(s, "64-bit capable", Bit(Str(cpu, "AddressWidth")));
        Add(s, "Virtualization firmware enabled", Bool(Str(cpu, "VirtualizationFirmwareEnabled")));
        Add(s, "SLAT (nested paging)", Bool(Str(cpu, "SecondLevelAddressTranslationExtensions")));
        return s;
    }

    // ---- composite formatters ----
    private static string FamilyLine(ManagementBaseObject cpu)
    {
        var fam = Str(cpu, "Family");
        var stepping = Str(cpu, "Stepping");
        var rev = Str(cpu, "Revision");
        var parts = new List<string>();
        if (fam != "—") parts.Add($"family {fam}");
        if (stepping != "—") parts.Add($"stepping {stepping}");
        if (rev != "—") parts.Add($"rev {rev}");
        return parts.Count > 0 ? string.Join("  ·  ", parts) : "—";
    }

    private static string WidthLine(ManagementBaseObject cpu)
    {
        var data = Str(cpu, "DataWidth");
        var addr = Str(cpu, "AddressWidth");
        if (data == "—" && addr == "—") return "—";
        var d = data == "—" ? "—" : $"{data}-bit";
        var a = addr == "—" ? "—" : $"{addr}-bit";
        return $"data {d}  ·  address {a}";
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

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    private static void Add(InfoSection s, string label, string value)
    {
        if (!string.IsNullOrEmpty(value) && value != "—")
            s.Items.Add(new(label, value));
    }

    // ---- decoders / converters ----
    private static string Mhz(string s) => double.TryParse(s, out var v) && v > 0 ? $"{v / 1000.0:0.00} GHz" : "—";

    private static string Kb(string s) =>
        double.TryParse(s, out var v) && v > 0
            ? (v >= 1024 ? $"{v / 1024.0:0.#} MB" : $"{v} KB")
            : "—";

    private static string Architecture(string s) => s switch
    {
        "0" => "x86 (32-bit)",
        "1" => "MIPS",
        "2" => "Alpha",
        "3" => "PowerPC",
        "5" => "ARM",
        "6" => "Itanium (IA-64)",
        "9" => "x64 (AMD64/Intel 64)",
        "12" => "ARM64",
        _ => "—",
    };

    private static string Bool(string s) => s switch
    {
        "True" or "true" or "1" => "Yes",
        "False" or "false" or "0" => "No",
        _ => "—",
    };

    // 64-bit capability inferred from the address width reported by the processor.
    private static string Bit(string addrWidth) => addrWidth switch
    {
        "64" => "Yes (64-bit)",
        "32" => "No (32-bit)",
        _ => "—",
    };
}
