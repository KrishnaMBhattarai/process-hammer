using System.IO;
using System.Management;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// Collects storage detail as pretty, well-spaced sections: one section per physical disk
/// (via the modern root\Microsoft\Windows\Storage namespace, falling back to Win32_DiskDrive)
/// and one "Volumes" section (via System.IO.DriveInfo). Every query is defensive: a missing
/// namespace, class or property yields "—" or a skipped row rather than throwing, so partial
/// data still renders. <see cref="Collect"/> never throws.
/// </summary>
public static class StorageInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        try { sections.AddRange(PhysicalDiskSections()); } catch { /* never throw */ }
        try { sections.Add(VolumesSection()); } catch { /* never throw */ }
        return sections;
    }

    // ---- Physical disks: one section each ----

    private static List<InfoSection> PhysicalDiskSections()
    {
        // Best source: the modern Storage namespace. May be unavailable on some systems.
        var disks = StorageQuery("MSFT_PhysicalDisk");
        if (disks.Count > 0)
            return disks.Select(ModernDiskSection).ToList();

        // Fallback: legacy Win32_DiskDrive.
        var legacy = All("Win32_DiskDrive");
        if (legacy.Count > 0)
            return legacy.Select(LegacyDiskSection).ToList();

        return new() { new InfoSection { Title = "Disks", Items = { new("Disks", "—") } } };
    }

    private static InfoSection ModernDiskSection(ManagementBaseObject d, int index)
    {
        var name = Str(d, "FriendlyName");
        var s = new InfoSection { Title = $"Disk {index + 1} — {name}" };

        Add(s, "Model", name);
        Add(s, "Type", DiskType(Str(d, "MediaType"), Str(d, "BusType"), Str(d, "SpindleSpeed")));
        Add(s, "Size", ulong.TryParse(Str(d, "Size"), out var sz) ? Bytes(sz) : "—");
        Add(s, "Bus", BusType(Str(d, "BusType")));
        Add(s, "Health", HealthStatus(Str(d, "HealthStatus")));
        Add(s, "Firmware", Str(d, "FirmwareVersion"));
        Add(s, "Serial", Str(d, "SerialNumber"));

        if (s.Items.Count == 0) s.Items.Add(new("Disk", "—"));
        return s;
    }

    private static InfoSection LegacyDiskSection(ManagementBaseObject d, int index)
    {
        var model = Str(d, "Model");
        var s = new InfoSection { Title = $"Disk {index + 1} — {model}" };

        Add(s, "Model", model);
        Add(s, "Type", LegacyType(Str(d, "InterfaceType")));
        Add(s, "Size", ulong.TryParse(Str(d, "Size"), out var sz) ? Bytes(sz) : "—");
        Add(s, "Bus", Str(d, "InterfaceType"));
        Add(s, "Firmware", Str(d, "FirmwareRevision"));
        Add(s, "Serial", Str(d, "SerialNumber"));

        if (s.Items.Count == 0) s.Items.Add(new("Disk", "—"));
        return s;
    }

    // ---- Volumes ----

    private static InfoSection VolumesSection()
    {
        var s = new InfoSection { Title = "Volumes" };
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { drives = Array.Empty<DriveInfo>(); }

        foreach (var d in drives)
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;

                var total = d.TotalSize;
                var free = d.TotalFreeSpace;
                var used = total - free;
                var pctFree = total > 0 ? free * 100.0 / total : 0;

                var letter = d.Name.TrimEnd('\\');
                var vol = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $" {d.VolumeLabel.Trim()}";
                var fmt = string.IsNullOrWhiteSpace(d.DriveFormat) ? "—" : d.DriveFormat;

                s.Items.Add(new(
                    $"{letter}{vol}",
                    $"{Bytes(used)} / {Bytes(total)}  ({pctFree:0.#}% free)  ·  {fmt}"));
            }
            catch { /* skip volumes that vanish mid-enumeration */ }
        }

        if (s.Items.Count == 0)
            s.Items.Add(new("Volumes", "—"));
        return s;
    }

    // ---- Section helper ----

    /// <summary>Append a row only when the value is present (not "—" / empty).</summary>
    private static void Add(InfoSection s, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "—")
            s.Items.Add(new(label, value));
    }

    // ---- WMI helpers ----

    /// <summary>Query the default (root\CIMV2) namespace.</summary>
    private static List<ManagementBaseObject> All(string wmiClass)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {wmiClass}");
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch { return new(); }
    }

    /// <summary>Query the modern storage namespace root\Microsoft\Windows\Storage.</summary>
    private static List<ManagementBaseObject> StorageQuery(string wmiClass)
    {
        try
        {
            var scope = new ManagementScope(@"root\Microsoft\Windows\Storage");
            using var searcher = new ManagementObjectSearcher(scope, new SelectQuery($"SELECT * FROM {wmiClass}"));
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch { return new(); }
    }

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    // ---- Decoders ----

    /// <summary>MSFT_PhysicalDisk.MediaType: 3=HDD, 4=SSD, 5=SCM, 0/other=unspecified.</summary>
    private static string MediaType(string s) => s switch
    {
        "3" => "HDD",
        "4" => "SSD",
        "5" => "SCM",
        _ => "",
    };

    /// <summary>MSFT_PhysicalDisk.BusType decode.</summary>
    private static string BusType(string s) => s switch
    {
        "1" => "SCSI",
        "2" => "ATAPI",
        "3" => "ATA",
        "4" => "1394",
        "5" => "SSA",
        "6" => "Fibre Channel",
        "7" => "USB",
        "8" => "RAID",
        "9" => "iSCSI",
        "10" => "SAS",
        "11" => "SATA",
        "12" => "SD",
        "13" => "MMC",
        "15" => "File-Backed Virtual",
        "16" => "Storage Spaces",
        "17" => "NVMe",
        _ => "—",
    };

    /// <summary>MSFT_PhysicalDisk.HealthStatus: 0=Healthy, 1=Warning, 2=Unhealthy.</summary>
    private static string HealthStatus(string s) => s switch
    {
        "0" => "Healthy",
        "1" => "Warning",
        "2" => "Unhealthy",
        _ => "—",
    };

    /// <summary>
    /// Combine MediaType, BusType and SpindleSpeed into a friendly type. NVMe is a bus but always
    /// implies SSD, so it reads best as "NVMe SSD". Otherwise prefer the media type; when that's
    /// missing, infer HDD from a non-zero spindle speed, else fall back to the bus type.
    /// </summary>
    private static string DiskType(string mediaType, string busType, string spindleSpeed)
    {
        var media = MediaType(mediaType);

        if (busType == "17") // NVMe
            return media == "SSD" || media.Length == 0 ? "NVMe SSD" : $"NVMe {media}";

        if (media.Length > 0) return media;

        // No media type reported: a spinning disk reports a non-zero spindle speed.
        if (ulong.TryParse(spindleSpeed, out var rpm) && rpm > 0) return "HDD";

        var bus = BusType(busType);
        return bus == "—" ? "—" : bus;
    }

    /// <summary>Best-effort type label from a legacy Win32_DiskDrive.InterfaceType.</summary>
    private static string LegacyType(string iface) => iface switch
    {
        "—" => "—",
        _ => iface,
    };

    // ---- Formatting ----

    /// <summary>Format a byte count as GB or TB (binary), whichever reads cleaner.</summary>
    private static string Bytes(ulong bytes)
    {
        double gb = bytes / 1024.0 / 1024.0 / 1024.0;
        return gb >= 1024 ? $"{gb / 1024.0:0.##} TB" : $"{gb:0.#} GB";
    }

    private static string Bytes(long bytes) => Bytes(bytes < 0 ? 0UL : (ulong)bytes);
}
