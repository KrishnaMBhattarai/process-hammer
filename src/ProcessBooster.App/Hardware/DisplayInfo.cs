using System.Management;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects per-monitor display detail via WMI. Physical monitor identity/size lives in the
/// <c>root\wmi</c> namespace (WmiMonitorID / WmiMonitorBasicDisplayParams), which can be
/// inaccessible on some systems; the current desktop mode comes from Win32_VideoController in the
/// default namespace. Every query is defensive: a missing class/property yields "—" and Collect()
/// never throws, so whatever data is available still renders.
/// </summary>
public static class DisplayInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        try
        {
            var ids = Wmi(@"root\wmi", "WmiMonitorID");
            var pars = Wmi(@"root\wmi", "WmiMonitorBasicDisplayParams");

            // Enumerate physical monitors; join on InstanceName which is shared across both classes.
            var count = ids.Count;
            for (var i = 0; i < count; i++)
            {
                var id = ids[i];
                var instance = Str(id, "InstanceName");
                var par = pars.FirstOrDefault(p => Str(p, "InstanceName") == instance) ?? pars.ElementAtOrDefault(i);

                var friendly = Decode(id, "UserFriendlyName");
                var title = friendly is not "—" ? friendly : $"Monitor {i + 1}";
                var s = new InfoSection { Title = count > 1 ? $"{title}  ·  Monitor {i + 1}" : title };

                s.Items.Add(new("Name", friendly));
                s.Items.Add(new("Manufacturer", Decode(id, "ManufacturerName")));
                s.Items.Add(new("Product code", Decode(id, "ProductCodeID")));
                s.Items.Add(new("Serial number", Decode(id, "SerialNumberID")));
                s.Items.Add(new("Manufactured", Manufactured(Str(id, "YearOfManufacture"), Str(id, "WeekOfManufacture"))));
                s.Items.Add(new("Physical size", DiagonalInches(par)));
                s.Items.Add(new("Input type", InputType(par)));
                sections.Add(s);
            }
        }
        catch { /* root\wmi unavailable — fall through to the desktop section only. */ }

        try { sections.Add(DesktopSection()); }
        catch { /* ignore */ }

        return sections;
    }

    private static InfoSection DesktopSection()
    {
        // Primary controller = the one currently driving a desktop mode.
        var controllers = Wmi(null, "Win32_VideoController");
        var primary = controllers.FirstOrDefault(c => int.TryParse(Str(c, "CurrentHorizontalResolution"), out var h) && h > 0)
                      ?? controllers.FirstOrDefault();

        var s = new InfoSection { Title = "Desktop (active mode)" };
        s.Items.Add(new("Adapter", Str(primary, "Name")));
        s.Items.Add(new("Resolution",
            $"{Str(primary, "CurrentHorizontalResolution")} x {Str(primary, "CurrentVerticalResolution")}"));
        s.Items.Add(new("Refresh rate", Hz(Str(primary, "CurrentRefreshRate"))));
        s.Items.Add(new("Color depth", BitDepth(Str(primary, "CurrentBitsPerPixel"))));
        return s;
    }

    // ---- WMI helpers ----
    private static List<ManagementBaseObject> Wmi(string? ns, string wmiClass)
    {
        try
        {
            using var searcher = ns is null
                ? new ManagementObjectSearcher($"SELECT * FROM {wmiClass}")
                : new ManagementObjectSearcher(new ManagementScope(ns), new SelectQuery($"SELECT * FROM {wmiClass}"));
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch { return new(); }
    }

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    /// <summary>Decodes a UInt16[] code-point array (nonzero entries -> chars, nulls trimmed).</summary>
    private static string Decode(ManagementBaseObject? mo, string prop)
    {
        try
        {
            if (mo?[prop] is not ushort[] codes) return "—";
            var chars = codes.Where(c => c != 0).Select(c => (char)c).ToArray();
            var text = new string(chars).Trim();
            return text.Length > 0 ? text : "—";
        }
        catch { return "—"; }
    }

    private static string Manufactured(string year, string week)
    {
        var hasYear = int.TryParse(year, out var y) && y > 0;
        var hasWeek = int.TryParse(week, out var w) && w > 0;
        if (hasYear && hasWeek) return $"{y}  (week {w})";
        if (hasYear) return y.ToString();
        return "—";
    }

    /// <summary>Diagonal in inches from Max{Horizontal,Vertical}ImageSize (centimeters).</summary>
    private static string DiagonalInches(ManagementBaseObject? par)
    {
        if (par is null) return "—";
        var h = ToDouble(Str(par, "MaxHorizontalImageSize"));
        var v = ToDouble(Str(par, "MaxVerticalImageSize"));
        if (h <= 0 || v <= 0) return "—";
        var inches = Math.Sqrt(h * h + v * v) / 2.54;
        return $"{inches:0.#}\"  ({h:0} x {v:0} cm)";
    }

    private static string InputType(ManagementBaseObject? par)
    {
        var raw = Str(par, "VideoInputType");
        return raw switch
        {
            "0" => "Analog (VGA)",
            "1" => "Digital (HDMI/DP)",
            _ => "—",
        };
    }

    private static string Hz(string s) => double.TryParse(s, out var v) && v > 0 ? $"{v:0} Hz" : "—";
    private static string BitDepth(string s) => int.TryParse(s, out var v) && v > 0 ? $"{v}-bit" : "—";
    private static double ToDouble(string s) => double.TryParse(s, out var v) ? v : 0;
}
