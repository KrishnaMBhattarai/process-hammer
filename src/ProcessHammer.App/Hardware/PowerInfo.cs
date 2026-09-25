using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// Collects battery and power information that works on both laptops and desktops. Desktops simply
/// report "no battery" plus the AC power status and active power plan. Every query is defensive:
/// <see cref="Collect"/> never throws — a missing API/class/property yields "—" or a skipped section.
/// </summary>
public static class PowerInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>
        {
            PowerStatusSection(),
            PowerPlanSection(),
        };

        var batteries = All(@"root\cimv2", "Win32_Battery");
        sections.Add(BatterySection(batteries));

        if (batteries.Count > 0)
        {
            var health = BatteryHealthSection();
            if (health is not null)
                sections.Add(health);
        }

        return sections;
    }

    // ---- 1) Power status (kernel32 GetSystemPowerStatus) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    private static InfoSection PowerStatusSection()
    {
        var section = new InfoSection { Title = "Power status" };
        try
        {
            if (GetSystemPowerStatus(out var s))
            {
                section.Items.Add(new("AC power", AcLine(s.ACLineStatus)));
                section.Items.Add(new("Battery charge",
                    s.BatteryLifePercent <= 100 ? $"{s.BatteryLifePercent}%" : "—"));
                section.Items.Add(new("Estimated runtime",
                    s.ACLineStatus == 1 ? "on AC" : Runtime(s.BatteryLifeTime)));
                section.Items.Add(new("Power saver", s.SystemStatusFlag == 1 ? "On" : "Off"));
            }
            else
            {
                section.Items.Add(new("Power status", "—"));
            }
        }
        catch
        {
            section.Items.Add(new("Power status", "—"));
        }
        return section;
    }

    private static string AcLine(byte v) => v switch
    {
        0 => "On battery (offline)",
        1 => "Plugged in (online)",
        _ => "Unknown",
    };

    private static string Runtime(int seconds)
    {
        if (seconds < 0) return "—";
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        return $"{h}h {m}m";
    }

    // ---- 2) Active power plan ----

    private static InfoSection PowerPlanSection()
    {
        var section = new InfoSection { Title = "Active power plan" };
        var name = ActivePlanFromWmi() ?? ActivePlanFromPowercfg() ?? "—";
        section.Items.Add(new("Plan", name));
        return section;
    }

    private static string? ActivePlanFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\cimv2\power"),
                new SelectQuery("SELECT * FROM Win32_PowerPlan WHERE IsActive = true"));
            foreach (var mo in searcher.Get())
            {
                var name = Str(mo, "ElementName");
                if (name is not "—" and not "")
                    return name;
            }
        }
        catch { }
        return null;
    }

    private static string? ActivePlanFromPowercfg()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/getactivescheme",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            // Format: "Power Scheme GUID: <guid>  (<name>)"
            var open = output.IndexOf('(');
            var close = output.IndexOf(')', open + 1 < 0 ? 0 : open + 1);
            if (open >= 0 && close > open)
            {
                var name = output.Substring(open + 1, close - open - 1).Trim();
                if (name.Length > 0) return name;
            }
        }
        catch { }
        return null;
    }

    // ---- 3) Battery (Win32_Battery) ----

    private static InfoSection BatterySection(List<ManagementBaseObject> batteries)
    {
        var section = new InfoSection { Title = "Battery" };
        if (batteries.Count == 0)
        {
            section.Items.Add(new("Battery", "No battery (desktop system)"));
            return section;
        }

        var i = 1;
        var multiple = batteries.Count > 1;
        foreach (var b in batteries)
        {
            var prefix = multiple ? $"Battery {i++} " : "";
            section.Items.Add(new($"{prefix}Name", Str(b, "Name")));
            section.Items.Add(new($"{prefix}Charge",
                uint.TryParse(Str(b, "EstimatedChargeRemaining"), out var pct) ? $"{pct}%" : "—"));
            section.Items.Add(new($"{prefix}Status", BatteryStatus(Str(b, "BatteryStatus"))));
            section.Items.Add(new($"{prefix}Design voltage", Volts(Str(b, "DesignVoltage"))));
            section.Items.Add(new($"{prefix}Chemistry", Chemistry(Str(b, "Chemistry"))));
        }
        return section;
    }

    private static string BatteryStatus(string s) => s switch
    {
        "1" => "Discharging",
        "2" => "On AC",
        "3" => "Fully charged",
        "4" => "Low",
        "5" => "Critical",
        "6" => "Charging",
        "7" => "Charging (high)",
        "8" => "Charging (low)",
        "9" => "Charging (critical)",
        "10" => "Undefined",
        "11" => "Partially charged",
        _ => "—",
    };

    private static string Chemistry(string s) => s switch
    {
        "1" => "Other",
        "2" => "Unknown",
        "3" => "Lead Acid",
        "4" => "Nickel Cadmium",
        "5" => "Nickel Metal Hydride",
        "6" => "Lithium-ion",
        "7" => "Zinc Air",
        "8" => "Lithium Polymer",
        _ => "—",
    };

    private static string Volts(string mv) =>
        double.TryParse(mv, out var v) && v > 0 ? $"{v / 1000.0:0.##} V" : "—";

    // ---- 4) Battery health (root\wmi) ----

    private static InfoSection? BatteryHealthSection()
    {
        var designed = FirstUlong(@"root\wmi", "BatteryStaticData", "DesignedCapacity");
        var full = FirstUlong(@"root\wmi", "BatteryFullChargedCapacity", "FullChargedCapacity");
        var cycles = FirstUlong(@"root\wmi", "BatteryCycleCount", "CycleCount");

        // If none of the root\wmi battery classes are available, skip the section entirely.
        if (designed is null && full is null && cycles is null)
            return null;

        var section = new InfoSection { Title = "Battery health" };
        section.Items.Add(new("Design capacity", Mwh(designed)));
        section.Items.Add(new("Full-charge capacity", Mwh(full)));

        if (designed is > 0 && full is not null)
        {
            var wear = (designed.Value - (double)full.Value) / designed.Value * 100.0;
            if (wear < 0) wear = 0;
            section.Items.Add(new("Wear", $"{wear:0.#}%"));
        }
        else
        {
            section.Items.Add(new("Wear", "—"));
        }

        section.Items.Add(new("Cycle count", cycles?.ToString() ?? "—"));
        return section;
    }

    private static string Mwh(ulong? v) => v is > 0 ? $"{v.Value} mWh" : "—";

    // ---- WMI helpers ----

    private static List<ManagementBaseObject> All(string scope, string wmiClass)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(scope),
                new SelectQuery($"SELECT * FROM {wmiClass}"));
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch { return new(); }
    }

    private static ulong? FirstUlong(string scope, string wmiClass, string prop)
    {
        foreach (var mo in All(scope, wmiClass))
        {
            if (ulong.TryParse(Str(mo, prop), out var v))
                return v;
        }
        return null;
    }

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }
}
