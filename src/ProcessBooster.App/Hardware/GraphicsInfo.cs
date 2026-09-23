using System.Management;
using Microsoft.Win32;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects rich per-adapter GPU detail via WMI (<c>Win32_VideoController</c>), producing one
/// <see cref="InfoSection"/> per display adapter (integrated + discrete are enumerated separately).
/// Because WMI's <c>AdapterRAM</c> is a 32-bit value capped at ~4 GB and therefore wrong for large
/// GPUs, true VRAM is read from the registry (<c>HardwareInformation.qwMemorySize</c>) when available.
/// Every access is defensive: a missing class/property/key yields "—" and <see cref="Collect"/> never throws.
/// </summary>
public static class GraphicsInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        try
        {
            var adapters = All("Win32_VideoController");
            var vram = RegistryVram();
            var multiple = adapters.Count > 1;
            var i = 1;
            foreach (var g in adapters)
            {
                var name = Str(g, "Name");
                var title = multiple ? $"GPU {i++}: {(name == "—" ? "Adapter" : name)}" : (name == "—" ? "Graphics" : name);
                var s = new InfoSection { Title = title };

                s.Items.Add(new("Model", name));
                s.Items.Add(new("Vendor", Str(g, "AdapterCompatibility")));
                s.Items.Add(new("Video processor", Str(g, "VideoProcessor")));

                s.Items.Add(new("VRAM", Vram(name, Str(g, "AdapterRAM"), vram)));

                s.Items.Add(new("Driver version", Str(g, "DriverVersion")));
                s.Items.Add(new("Driver date", WmiDate(Str(g, "DriverDate"))));

                var w = Str(g, "CurrentHorizontalResolution");
                var h = Str(g, "CurrentVerticalResolution");
                var hz = Str(g, "CurrentRefreshRate");
                var bpp = Str(g, "CurrentBitsPerPixel");
                var mode = (w == "—" || h == "—")
                    ? "—"
                    : $"{w} x {h} @ {hz} Hz  ·  {bpp}-bit";
                s.Items.Add(new("Current mode", mode));
                s.Items.Add(new("Status", Str(g, "Status")));

                sections.Add(s);
            }
        }
        catch { /* never throw */ }
        return sections;
    }

    // ---- VRAM resolution ----

    /// <summary>True VRAM from the registry if a name-matched entry exists; otherwise the WMI
    /// AdapterRAM value (flagged, since it is capped at ~4 GB and often understated).</summary>
    private static string Vram(string name, string adapterRam, List<(string Desc, ulong Bytes)> reg)
    {
        if (name != "—")
        {
            foreach (var (desc, bytes) in reg)
            {
                if (bytes > 0 && (Contains(desc, name) || Contains(name, desc)))
                    return Gb(bytes);
            }
        }
        if (ulong.TryParse(adapterRam, out var wmiBytes) && wmiBytes > 0)
            return $"{Gb(wmiBytes)} (WMI, may be capped at 4 GB)";
        return "—";
    }

    /// <summary>Enumerates the display-adapter class key, reading DriverDesc + qwMemorySize from
    /// each numeric subkey (0000, 0001, …).</summary>
    private static List<(string Desc, ulong Bytes)> RegistryVram()
    {
        var result = new List<(string, ulong)>();
        try
        {
            const string path = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var classKey = baseKey.OpenSubKey(path);
            if (classKey is null) return result;

            foreach (var sub in classKey.GetSubKeyNames())
            {
                if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;
                try
                {
                    using var k = classKey.OpenSubKey(sub);
                    if (k is null) continue;
                    var desc = k.GetValue("DriverDesc")?.ToString()?.Trim() ?? "";
                    ulong bytes = 0;
                    if (k.GetValue("HardwareInformation.qwMemorySize") is { } raw)
                    {
                        try { bytes = Convert.ToUInt64(raw); } catch { bytes = 0; }
                    }
                    if (desc.Length > 0 || bytes > 0)
                        result.Add((desc, bytes));
                }
                catch { /* skip subkey */ }
            }
        }
        catch { /* registry unavailable */ }
        return result;
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

    private static bool Contains(string haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack) && !string.IsNullOrWhiteSpace(needle) &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string Gb(ulong bytes) => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.#} GB";

    private static string WmiDate(string wmi)
    {
        // WMI datetime: yyyymmddHHMMSS.ffffff+zzz  — take the date portion as yyyy-MM-dd.
        if (wmi.Length >= 8 && long.TryParse(wmi[..8], out _))
            return $"{wmi[..4]}-{wmi.Substring(4, 2)}-{wmi.Substring(6, 2)}";
        return wmi is "—" or "" ? "—" : wmi;
    }
}
