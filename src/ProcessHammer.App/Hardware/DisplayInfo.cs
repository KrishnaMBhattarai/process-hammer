using System.Management;
using System.Runtime.InteropServices;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// Collects per-monitor display detail. The physical <b>connection type</b> (HDMI / DisplayPort /
/// DVI / VGA / internal) can only be read reliably from the Windows DisplayConfig API
/// (user32.dll), which is the primary source here: it enumerates the active display paths and
/// yields each target's friendly name, connector technology, active resolution, refresh rate,
/// orientation, primary flag, and advanced-colour state (HDR + bits-per-channel + colour encoding).
/// WMI (<c>root\wmi</c> WmiMonitorID / WmiMonitorBasicDisplayParams) supplements this with the
/// manufacturer, physical diagonal, and year of manufacture, best-effort matched by friendly name.
/// Every call is defensive: any failure degrades to "—" and <see cref="Collect"/> never throws.
/// </summary>
public static class DisplayInfoService
{
    public static List<InfoSection> Collect()
    {
        List<DisplayMonitor> monitors;
        List<WmiMonitor> wmi;

        // Absolute guarantee: Collect() never throws, whatever DisplayConfig/WMI do.
        try { monitors = QueryDisplayConfigMonitors(); } catch { monitors = new(); }
        try { wmi = QueryWmiMonitors(); } catch { wmi = new(); }

        var sections = new List<InfoSection>();

        if (monitors.Count > 0)
        {
            sections.Add(Summary(monitors.Count));

            for (var i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                var title = m.FriendlyName is not "—" ? m.FriendlyName : $"Display {i + 1}";
                var s = new InfoSection { Title = title };

                // Best-effort correlate a WMI record by friendly name; else fall back positionally.
                var match = MatchWmi(wmi, m.FriendlyName, i);

                s.Items.Add(new("Name", m.FriendlyName));
                s.Items.Add(new("Connection", m.Connection));
                s.Items.Add(new("Resolution", m.Resolution));
                s.Items.Add(new("Refresh", m.Refresh));
                s.Items.Add(new("HDR", m.Hdr));
                s.Items.Add(new("Color depth", m.ColorDepth));
                s.Items.Add(new("Orientation", m.Orientation));
                s.Items.Add(new("Primary", m.Primary));
                s.Items.Add(new("Physical size", match?.Size ?? "—"));
                s.Items.Add(new("Manufacturer", match?.Manufacturer ?? "—"));
                s.Items.Add(new("Year of manufacture", match?.Year ?? "—"));
                sections.Add(s);
            }
            return sections;
        }

        // DisplayConfig unavailable — degrade to a WMI-only section per monitor.
        if (wmi.Count > 0)
        {
            sections.Add(Summary(wmi.Count));

            for (var i = 0; i < wmi.Count; i++)
            {
                var w = wmi[i];
                var title = w.FriendlyName is not "—" ? w.FriendlyName : $"Display {i + 1}";
                var s = new InfoSection { Title = title };
                s.Items.Add(new("Name", w.FriendlyName));
                s.Items.Add(new("Connection", "—"));
                s.Items.Add(new("Resolution", "—"));
                s.Items.Add(new("Refresh", "—"));
                s.Items.Add(new("HDR", "—"));
                s.Items.Add(new("Color depth", "—"));
                s.Items.Add(new("Orientation", "—"));
                s.Items.Add(new("Primary", "—"));
                s.Items.Add(new("Physical size", w.Size));
                s.Items.Add(new("Manufacturer", w.Manufacturer));
                s.Items.Add(new("Year of manufacture", w.Year));
                sections.Add(s);
            }
        }

        return sections;
    }

    /// <summary>Top-of-list overview card: how many active monitors were found.</summary>
    private static InfoSection Summary(int count)
    {
        var s = new InfoSection { Title = "Displays" };
        s.Items.Add(new("Active monitors", count.ToString()));
        return s;
    }

    // =====================================================================================
    //  DisplayConfig (user32.dll) — primary source for connector type + active mode.
    // =====================================================================================

    private sealed record DisplayMonitor(
        string FriendlyName,
        string Connection,
        string Resolution,
        string Refresh,
        string Hdr,
        string ColorDepth,
        string Orientation,
        string Primary);

    private static List<DisplayMonitor> QueryDisplayConfigMonitors()
    {
        var result = new List<DisplayMonitor>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var numPaths, out var numModes) != ERROR_SUCCESS)
                return result;
            if (numPaths == 0)
                return result;

            var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
            var modes = new DISPLAYCONFIG_MODE_INFO[numModes];

            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != ERROR_SUCCESS)
                return result;

            for (var p = 0; p < numPaths; p++)
            {
                var path = paths[p];

                // Friendly name + connector technology for this target.
                var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id,
                    },
                };

                var friendly = "—";
                var connection = "—";
                if (DisplayConfigGetDeviceInfo(ref name) == ERROR_SUCCESS)
                {
                    friendly = Clean(name.monitorFriendlyDeviceName);
                    connection = OutputTechnology(name.outputTechnology);
                }
                else
                {
                    // Fall back to the connector info carried on the path itself.
                    connection = OutputTechnology(path.targetInfo.outputTechnology);
                }

                var (resolution, refresh) = ModeFor(path, modes);
                var (hdr, depth) = AdvancedColorFor(path);
                var orientation = Orientation(path.targetInfo.rotation);
                var primary = PrimaryFor(path, modes);

                result.Add(new DisplayMonitor(
                    friendly, connection, resolution, refresh, hdr, depth, orientation, primary));
            }
        }
        catch { /* DisplayConfig unavailable — caller degrades to WMI-only. */ }

        return result;
    }

    /// <summary>Active resolution and refresh rate for a path's target mode, each best-effort.</summary>
    private static (string Resolution, string Refresh) ModeFor(DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        try
        {
            var idx = path.targetInfo.modeInfoIdx;
            if (idx < modes.Length && modes[idx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
            {
                var sig = modes[idx].targetMode.targetVideoSignalInfo;
                var w = sig.activeSize.cx;
                var h = sig.activeSize.cy;
                var resolution = w > 0 && h > 0 ? $"{w} x {h}" : "—";
                var hz = RefreshHz(sig.vSyncFreq);
                var refresh = hz > 0 ? $"{hz} Hz" : "—";
                return (resolution, refresh);
            }
            return ("—", "—");
        }
        catch { return ("—", "—"); }
    }

    /// <summary>
    /// HDR state and colour depth via DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO (header type = 9).
    /// The <c>value</c> field is a bitfield: bit0 advancedColorSupported, bit1 advancedColorEnabled,
    /// bit2 wideColorEnforced, bit3 advancedColorForceDisabled.
    /// </summary>
    private static (string Hdr, string ColorDepth) AdvancedColorFor(DISPLAYCONFIG_PATH_INFO path)
    {
        try
        {
            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id,
                },
            };

            if (DisplayConfigGetDeviceInfo(ref info) != ERROR_SUCCESS)
                return ("—", "—");

            var supported = (info.value & 0x1) != 0;
            var enabled = (info.value & 0x2) != 0;

            var hdr = enabled ? "On" : supported ? "Supported (off)" : "Not supported";

            var depth = "—";
            if (info.bitsPerColorChannel > 0)
            {
                depth = $"{info.bitsPerColorChannel}-bit";
                var enc = ColorEncoding(info.colorEncoding);
                if (enc is not null)
                    depth = $"{depth}  ({enc})";
            }

            return (hdr, depth);
        }
        catch { return ("—", "—"); }
    }

    /// <summary>A path is primary when its source desktop position is at the origin (0,0).</summary>
    private static string PrimaryFor(DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        try
        {
            var idx = path.sourceInfo.modeInfoIdx;
            if (idx < modes.Length && modes[idx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                var pos = modes[idx].sourceMode.position;
                return pos.x == 0 && pos.y == 0 ? "Yes" : "No";
            }
            return "—";
        }
        catch { return "—"; }
    }

    private static int RefreshHz(DISPLAYCONFIG_RATIONAL r)
    {
        if (r.Denominator == 0) return 0;
        return (int)Math.Round((double)r.Numerator / r.Denominator);
    }

    private static string Orientation(uint rotation) => rotation switch
    {
        1 => "Landscape",
        2 => "Portrait (90°)",
        3 => "Landscape (flipped 180°)",
        4 => "Portrait (270°)",
        _ => "—",
    };

    /// <summary>Decodes DISPLAYCONFIG_COLOR_ENCODING; null when not worth surfacing.</summary>
    private static string? ColorEncoding(int encoding) => encoding switch
    {
        0 => "RGB",
        1 => "YCbCr444",
        2 => "YCbCr422",
        3 => "YCbCr420",
        4 => "Intensity",
        _ => null,
    };

    private static string OutputTechnology(uint tech) => unchecked((int)tech) switch
    {
        unchecked((int)0xFFFFFFFF) => "Other",
        0 => "VGA",
        1 => "S-Video",
        2 => "Composite",
        3 => "Component",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS (internal)",
        8 => "D-Jpn",
        9 => "SDI",
        10 => "DisplayPort (external)",
        11 => "DisplayPort (embedded)",
        12 => "UDI (external)",
        13 => "UDI (embedded)",
        15 => "Internal",
        unchecked((int)0x80000000) => "Internal",
        _ => "—",
    };

    // =====================================================================================
    //  WMI (root\wmi) — supplement: manufacturer, physical size, year of manufacture.
    // =====================================================================================

    private sealed record WmiMonitor(string FriendlyName, string Manufacturer, string Size, string Year);

    private static List<WmiMonitor> QueryWmiMonitors()
    {
        var list = new List<WmiMonitor>();
        try
        {
            var ids = Wmi(@"root\wmi", "WmiMonitorID");
            var pars = Wmi(@"root\wmi", "WmiMonitorBasicDisplayParams");

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var instance = Str(id, "InstanceName");
                var par = pars.FirstOrDefault(p => Str(p, "InstanceName") == instance) ?? pars.ElementAtOrDefault(i);

                list.Add(new WmiMonitor(
                    FriendlyName: Decode(id, "UserFriendlyName"),
                    Manufacturer: Decode(id, "ManufacturerName"),
                    Size: DiagonalInches(par),
                    Year: Year(Str(id, "YearOfManufacture"))));
            }
        }
        catch { /* root\wmi unavailable. */ }
        return list;
    }

    private static WmiMonitor? MatchWmi(List<WmiMonitor> wmi, string friendly, int index)
    {
        if (wmi.Count == 0) return null;
        if (friendly is not "—")
        {
            var hit = wmi.FirstOrDefault(w =>
                string.Equals(w.FriendlyName, friendly, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return wmi.ElementAtOrDefault(index);
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

    private static string Year(string year)
        => int.TryParse(year, out var y) && y > 0 ? y.ToString() : "—";

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

    private static double ToDouble(string s) => double.TryParse(s, out var v) ? v : 0;

    private static string Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? "—" : t;
    }

    // =====================================================================================
    //  P/Invoke surface + struct definitions (DisplayConfig).
    // =====================================================================================

    private const int ERROR_SUCCESS = 0;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const uint DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1;
    private const uint DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public int LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;      // packed AdditionalSignalInfo union
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    // Source mode: a 2D pixel region plus a pixel format, then the desktop-space top-left position.
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public POINTL position;
    }

    // The native DISPLAYCONFIG_MODE_INFO is: header (infoType,id,adapterId) then an 8-byte-aligned
    // union of target/source/desktopImage modes. Header occupies 16 bytes, so the union starts at
    // offset 16. We overlay the target and source mode fields (the two we read) at that offset.
    [StructLayout(LayoutKind.Explicit)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    // Advanced-colour (HDR / wide-gamut) query. value is a bitfield (see AdvancedColorFor);
    // colorEncoding is DISPLAYCONFIG_COLOR_ENCODING; bitsPerColorChannel is the active bit depth.
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public int colorEncoding;
        public uint bitsPerColorChannel;
    }
}
