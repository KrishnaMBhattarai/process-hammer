using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// Collects a rich Operating System inventory from WMI (Win32_OperatingSystem) and the registry.
/// Every read is defensive: a missing class, property, key or value yields "—" rather than
/// throwing, so <see cref="Collect"/> never throws and partial data still renders.
/// </summary>
public static class OsInfoService
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        try { sections.Add(WindowsSection()); } catch { /* never throw */ }
        try { sections.Add(InstallUptimeSection()); } catch { /* never throw */ }
        try { sections.Add(RegistrationSection()); } catch { /* never throw */ }
        try { sections.Add(EnvironmentSection()); } catch { /* never throw */ }
        return sections;
    }

    private static InfoSection WindowsSection()
    {
        var s = new InfoSection { Title = "Windows" };

        var productName = Reg(CurrentVersionKey, "ProductName");
        var currentBuild = Reg(CurrentVersionKey, "CurrentBuild");
        var ubr = Reg(CurrentVersionKey, "UBR");

        // On Windows 11 ProductName may still read "Windows 10"; derive the display name from the
        // OS build (>= 22000 is Windows 11) while keeping the raw registry edition.
        var edition = productName;
        if (int.TryParse(currentBuild, out var build) && build >= 22000 &&
            productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            edition = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        s.Items.Add(new("Edition", edition));
        if (!string.Equals(edition, productName, StringComparison.Ordinal))
            s.Items.Add(new("Reported edition", productName));

        s.Items.Add(new("Display version", Reg(CurrentVersionKey, "DisplayVersion")));

        var versionBuild = currentBuild is "—"
            ? "—"
            : (ubr is "—" ? currentBuild : $"{currentBuild}.{ubr}");
        s.Items.Add(new("Version / build", versionBuild));

        s.Items.Add(new("Architecture", RuntimeInformation.OSArchitecture.ToString()));
        s.Items.Add(new("Edition ID", Reg(CurrentVersionKey, "EditionID")));
        return s;
    }

    private static InfoSection InstallUptimeSection()
    {
        var os = First("Win32_OperatingSystem");
        var s = new InfoSection { Title = "Install & uptime" };

        s.Items.Add(new("Install date", WmiDate(Str(os, "InstallDate"))));
        s.Items.Add(new("Last boot", WmiDateTime(Str(os, "LastBootUpTime"))));

        var boot = WmiToDateTime(Str(os, "LastBootUpTime"));
        s.Items.Add(new("Uptime", boot is { } b ? FormatUptime(DateTime.Now - b) : "—"));
        return s;
    }

    private static InfoSection RegistrationSection()
    {
        var s = new InfoSection { Title = "Registration" };
        s.Items.Add(new("Registered owner", Reg(CurrentVersionKey, "RegisteredOwner")));
        s.Items.Add(new("Registered organization", Reg(CurrentVersionKey, "RegisteredOrganization")));
        s.Items.Add(new("Product ID", Reg(CurrentVersionKey, "ProductId")));
        s.Items.Add(new("Windows product ID", Reg(CurrentVersionKey, "ProductId")));
        return s;
    }

    private static InfoSection EnvironmentSection()
    {
        var s = new InfoSection { Title = "Environment" };
        s.Items.Add(new("Machine name", Safe(() => Environment.MachineName)));
        s.Items.Add(new("Current user", Safe(() => Environment.UserName)));
        s.Items.Add(new("System locale", Safe(() => CultureInfo.CurrentCulture.DisplayName)));
        s.Items.Add(new("Time zone", Safe(() => TimeZoneInfo.Local.DisplayName)));
        s.Items.Add(new(".NET runtime", Safe(() => RuntimeInformation.FrameworkDescription)));
        s.Items.Add(new("System directory", Safe(() => Environment.SystemDirectory)));
        s.Items.Add(new("Logical drives", Safe(() => Environment.GetLogicalDrives().Length.ToString())));
        s.Items.Add(new("Boot mode", BootMode()));
        s.Items.Add(new("Secure Boot", SecureBoot()));
        return s;
    }

    private static string BootMode() => Reg(@"SYSTEM\CurrentControlSet\Control", "PEFirmwareType") switch
    {
        "2" => "UEFI",
        "1" => "Legacy BIOS",
        _ => "—",
    };

    private static string SecureBoot() => Reg(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled") switch
    {
        "1" => "On",
        "0" => "Off",
        _ => "—",
    };

    // ---- WMI helpers ----
    private static ManagementBaseObject? First(string wmiClass)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {wmiClass}");
            return searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
        }
        catch { return null; }
    }

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    // ---- Registry helper ----
    private static string Reg(string subKey, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            var value = key?.GetValue(valueName);
            return value?.ToString()?.Trim() is { Length: > 0 } v ? v : "—";
        }
        catch { return "—"; }
    }

    private static string Safe(Func<string> f)
    {
        try { return f() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    // ---- WMI datetime decoding: yyyymmddHHMMSS.ffffff+zzz ----
    private static DateTime? WmiToDateTime(string wmi)
    {
        try
        {
            return ManagementDateTimeConverter.ToDateTime(wmi);
        }
        catch
        {
            if (wmi.Length >= 14 &&
                DateTime.TryParseExact(wmi[..14], "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            return null;
        }
    }

    private static string WmiDate(string wmi)
        => WmiToDateTime(wmi) is { } dt ? dt.ToString("yyyy-MM-dd") : "—";

    private static string WmiDateTime(string wmi)
        => WmiToDateTime(wmi) is { } dt ? dt.ToString("yyyy-MM-dd HH:mm") : "—";

    private static string FormatUptime(TimeSpan up)
    {
        if (up < TimeSpan.Zero) up = TimeSpan.Zero;
        return $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m";
    }
}
