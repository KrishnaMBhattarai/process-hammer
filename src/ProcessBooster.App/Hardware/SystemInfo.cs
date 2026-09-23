using System.Management;
using System.Runtime.InteropServices;

namespace ProcessBooster.App.Hardware;

/// <summary>A label/value pair rendered as one row in a system card.</summary>
public sealed record InfoItem(string Label, string Value);

/// <summary>A titled group of <see cref="InfoItem"/>s rendered as one card.</summary>
public sealed class InfoSection
{
    public string Title { get; init; } = "";
    public List<InfoItem> Items { get; init; } = new();
}

/// <summary>
/// Collects the (mostly static) hardware/OS inventory via WMI/CIM. Every query is defensive: a
/// missing class or property yields "—" rather than throwing, so partial data still renders.
/// </summary>
public static class SystemInfoService
{
    public static List<InfoSection> Collect()
    {
        return new List<InfoSection>
        {
            SystemSection(),
            ProcessorSection(),
            MemorySection(),
            GraphicsSection(),
            MotherboardSection(),
            StorageSection(),
        };
    }

    private static InfoSection SystemSection()
    {
        var cs = First("Win32_ComputerSystem");
        var os = First("Win32_OperatingSystem");
        var s = new InfoSection { Title = "System" };
        s.Items.Add(new("Manufacturer", Str(cs, "Manufacturer")));
        s.Items.Add(new("Model", Str(cs, "Model")));
        s.Items.Add(new("Machine name", Environment.MachineName));
        s.Items.Add(new("Operating system", $"{Str(os, "Caption")} ({RuntimeInformation.OSArchitecture})"));
        s.Items.Add(new("OS version / build", $"{Str(os, "Version")}  (build {Str(os, "BuildNumber")})"));
        s.Items.Add(new("Installed", WmiDate(Str(os, "InstallDate"))));
        s.Items.Add(new("Last boot", WmiDate(Str(os, "LastBootUpTime"))));
        return s;
    }

    private static InfoSection ProcessorSection()
    {
        var cpu = First("Win32_Processor");
        var s = new InfoSection { Title = "Processor" };
        s.Items.Add(new("Model", Str(cpu, "Name")));
        s.Items.Add(new("Manufacturer", Str(cpu, "Manufacturer")));
        s.Items.Add(new("Cores / threads", $"{Str(cpu, "NumberOfCores")} cores  ·  {Str(cpu, "NumberOfLogicalProcessors")} threads"));
        s.Items.Add(new("Base clock", Mhz(Str(cpu, "MaxClockSpeed"))));
        s.Items.Add(new("Socket", Str(cpu, "SocketDesignation")));
        s.Items.Add(new("L2 cache", Kb(Str(cpu, "L2CacheSize"))));
        s.Items.Add(new("L3 cache", Kb(Str(cpu, "L3CacheSize"))));
        return s;
    }

    private static InfoSection MemorySection()
    {
        var cs = First("Win32_ComputerSystem");
        var s = new InfoSection { Title = "Memory" };
        if (ulong.TryParse(Str(cs, "TotalPhysicalMemory"), out var total))
            s.Items.Add(new("Total installed", Gb(total)));

        var i = 1;
        foreach (var m in All("Win32_PhysicalMemory"))
        {
            var cap = ulong.TryParse(Str(m, "Capacity"), out var c) ? Gb(c) : "—";
            var speed = Str(m, "Speed");
            var slot = Str(m, "DeviceLocator");
            var part = Str(m, "PartNumber").Trim();
            s.Items.Add(new($"Module {i++} ({slot})", $"{cap} @ {speed} MT/s  ·  {part}"));
        }
        return s;
    }

    private static InfoSection GraphicsSection()
    {
        var s = new InfoSection { Title = "Graphics" };
        var i = 1;
        foreach (var g in All("Win32_VideoController"))
        {
            var prefix = All("Win32_VideoController").Count > 1 ? $"GPU {i++} " : "";
            s.Items.Add(new($"{prefix}Model", Str(g, "Name")));
            s.Items.Add(new($"{prefix}Driver", $"{Str(g, "DriverVersion")}  ({WmiDate(Str(g, "DriverDate"))})"));
            s.Items.Add(new($"{prefix}Resolution", $"{Str(g, "CurrentHorizontalResolution")} x {Str(g, "CurrentVerticalResolution")} @ {Str(g, "CurrentRefreshRate")} Hz"));
        }
        return s;
    }

    private static InfoSection MotherboardSection()
    {
        var bb = First("Win32_BaseBoard");
        var bios = First("Win32_BIOS");
        var s = new InfoSection { Title = "Motherboard & BIOS" };
        s.Items.Add(new("Manufacturer", Str(bb, "Manufacturer")));
        s.Items.Add(new("Model", Str(bb, "Product")));
        s.Items.Add(new("Version", Str(bb, "Version")));
        s.Items.Add(new("BIOS", $"{Str(bios, "SMBIOSBIOSVersion")}  ({WmiDate(Str(bios, "ReleaseDate"))})"));
        return s;
    }

    private static InfoSection StorageSection()
    {
        var s = new InfoSection { Title = "Storage" };
        var i = 1;
        foreach (var d in All("Win32_DiskDrive"))
        {
            var size = ulong.TryParse(Str(d, "Size"), out var sz) ? Gb(sz) : "—";
            s.Items.Add(new($"Disk {i++}", $"{Str(d, "Model").Trim()}  ·  {size}"));
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

    private static string Mhz(string s) => double.TryParse(s, out var v) ? $"{v / 1000.0:0.00} GHz" : "—";
    private static string Kb(string s) => double.TryParse(s, out var v) && v > 0 ? (v >= 1024 ? $"{v / 1024.0:0.#} MB" : $"{v} KB") : "—";
    private static string Gb(ulong bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.#} GB";

    private static string WmiDate(string wmi)
    {
        // WMI datetime: yyyymmddHHMMSS.ffffff+zzz  — take the date portion.
        if (wmi.Length >= 8 && long.TryParse(wmi[..8], out _))
            return $"{wmi[..4]}-{wmi.Substring(4, 2)}-{wmi.Substring(6, 2)}";
        return wmi is "—" or "" ? "—" : wmi;
    }
}
