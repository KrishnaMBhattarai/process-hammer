using System.Management;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// Collects a Device-Manager-like inventory of present PnP devices via WMI (Win32_PnPEntity),
/// grouped by device class. Every query is defensive: <see cref="Collect"/> never throws, so a
/// missing class or property simply yields fewer rows rather than an error.
/// </summary>
public static class DevicesInfoService
{
    // Noisy/system PNPClass values that are not useful in a device list.
    private static readonly HashSet<string> SkipClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Computer", "Volume", "LegacyDriver", "SoftwareDevice", "SoftwareComponent",
        "Processor", "VolumeSnapshot", "SecurityDevices",
    };

    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        try
        {
            var devices = QueryPresentDevices();

            // Summary first.
            var summary = new InfoSection { Title = "Devices" };
            summary.Items.Add(new("Total present", devices.Count.ToString()));
            sections.Add(summary);

            // Group by class, one section per class.
            var groups = devices
                .GroupBy(d => d.PnpClass, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => Friendly(g.Key), StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                var s = new InfoSection { Title = Friendly(g.Key) };
                foreach (var d in g.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                    s.Items.Add(new(d.Name, d.Manufacturer == "—" ? "" : d.Manufacturer));
                sections.Add(s);
            }
        }
        catch { /* never throw: return whatever was gathered so far */ }

        return sections;
    }

    private readonly record struct Device(string Name, string Manufacturer, string PnpClass);

    // ---- WMI query ----
    private static List<Device> QueryPresentDevices()
    {
        var list = new List<Device>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, PNPClass, Status, Present FROM Win32_PnPEntity");
            foreach (var mo in searcher.Get().Cast<ManagementBaseObject>())
            {
                using (mo)
                {
                    var status = Str(mo, "Status");
                    var present = Bool(mo, "Present");
                    if (!(status == "OK" || present)) continue;

                    var name = Str(mo, "Name");
                    var cls = Str(mo, "PNPClass");
                    if (name == "—" || cls == "—") continue;
                    if (SkipClasses.Contains(cls)) continue;

                    list.Add(new Device(name, Str(mo, "Manufacturer"), cls));
                }
            }
        }
        catch { return list; }
        return list;
    }

    // ---- helpers ----
    private static string Friendly(string pnpClass) => pnpClass switch
    {
        "AudioEndpoint" or "Media" => "Audio",
        "Net" => "Network",
        "Camera" or "Image" => "Imaging",
        "Monitor" => "Monitors",
        "DiskDrive" => "Disks",
        "HIDClass" => "HID",
        _ => pnpClass,
    };

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    private static bool Bool(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop] is bool b && b; }
        catch { return false; }
    }
}
