using System.IO;
using System.Management;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects storage detail: physical disks (via the modern root\Microsoft\Windows\Storage
/// namespace, falling back to Win32_DiskDrive) and mounted volumes (via System.IO.DriveInfo).
/// Every query is defensive: a missing namespace, class or property yields "—" rather than
/// throwing, so partial data still renders. <see cref="Collect"/> never throws.
/// </summary>
public static class StorageInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        try { sections.Add(PhysicalDisksSection()); } catch { /* never throw */ }
        try { sections.Add(VolumesSection()); } catch { /* never throw */ }
        return sections;
    }

    // ---- Physical disks ----

    private static InfoSection PhysicalDisksSection()
    {
        var s = new InfoSection { Title = "Physical disks" };

        // Best source: the modern Storage namespace. May be unavailable on some systems.
        var disks = StorageQuery("MSFT_PhysicalDisk");
        if (disks.Count > 0)
        {
            var i = 1;
            foreach (var d in disks)
            {
                var name = Str(d, "FriendlyName");
                var type = DiskType(Str(d, "MediaType"), Str(d, "BusType"));
                var size = ulong.TryParse(Str(d, "Size"), out var sz) ? Bytes(sz) : "—";
                var health = HealthStatus(Str(d, "HealthStatus"));
                var bus = BusType(Str(d, "BusType"));
                s.Items.Add(new($"Disk {i++}", $"{name}  ·  {type}  ·  {size}  ·  {health}  ·  {bus}"));
            }
            return s;
        }

        // Fallback: legacy Win32_DiskDrive.
        var j = 1;
        foreach (var d in All("Win32_DiskDrive"))
        {
            var model = Str(d, "Model").Trim();
            var size = ulong.TryParse(Str(d, "Size"), out var sz) ? Bytes(sz) : "—";
            var iface = Str(d, "InterfaceType");
            var media = Str(d, "MediaType");
            var parts = Str(d, "Partitions");
            var serial = Str(d, "SerialNumber").Trim();
            s.Items.Add(new($"Disk {j++}",
                $"{model}  ·  {size}  ·  {iface}  ·  {media}  ·  {parts} partitions  ·  SN {serial}"));
        }

        if (s.Items.Count == 0)
            s.Items.Add(new("Disks", "—"));
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
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "—" : d.VolumeLabel.Trim();
                var fmt = string.IsNullOrWhiteSpace(d.DriveFormat) ? "—" : d.DriveFormat;

                s.Items.Add(new(
                    $"{d.Name}  ({label})",
                    $"{fmt}  ·  {Bytes(used)} used / {Bytes(total)} ({pctFree:0.#}% free)"));
            }
            catch { /* skip volumes that vanish mid-enumeration */ }
        }

        if (s.Items.Count == 0)
            s.Items.Add(new("Volumes", "—"));
        return s;
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
    /// Combine MediaType and BusType into a friendly type. NVMe is a bus (always SSD) but is the
    /// most useful label; otherwise prefer the media type, falling back to the bus type.
    /// </summary>
    private static string DiskType(string mediaType, string busType)
    {
        if (busType == "17") return "NVMe";
        var media = MediaType(mediaType);
        if (media.Length > 0) return media;
        var bus = BusType(busType);
        return bus == "—" ? "—" : bus;
    }

    // ---- Formatting ----

    /// <summary>Format a byte count as GB or TB (binary), whichever reads cleaner.</summary>
    private static string Bytes(ulong bytes)
    {
        double gb = bytes / 1024.0 / 1024.0 / 1024.0;
        return gb >= 1024 ? $"{gb / 1024.0:0.##} TB" : $"{gb:0.#} GB";
    }

    private static string Bytes(long bytes) => Bytes(bytes < 0 ? 0UL : (ulong)bytes);
}
