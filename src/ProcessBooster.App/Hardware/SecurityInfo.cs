using System.Management;
using Microsoft.Win32;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects a Windows security overview (Defender/AV, firewall, BitLocker, TPM, Secure Boot, UAC)
/// via WMI/CIM and the registry. Every read is individually defensive: a missing namespace, class,
/// property, or registry value yields "—" (or a section-specific fallback) rather than throwing, so
/// partial data still renders. <see cref="Collect"/> never throws.
/// </summary>
public static class SecurityInfoService
{
    public static List<InfoSection> Collect()
    {
        return new List<InfoSection>
        {
            AntivirusSection(),
            FirewallSection(),
            BitLockerSection(),
            TpmSection(),
            SecureBootUacSection(),
        };
    }

    // ---- 1) Antivirus / Defender ----
    private static InfoSection AntivirusSection()
    {
        var s = new InfoSection { Title = "Antivirus / Defender" };
        try
        {
            var status = First(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus");
            if (status is not null)
            {
                s.Items.Add(new("Antivirus", OnOff(Str(status, "AntivirusEnabled"))));
                s.Items.Add(new("Real-time protection", OnOff(Str(status, "RealTimeProtectionEnabled"))));
                s.Items.Add(new("Antispyware", OnOff(Str(status, "AntispywareEnabled"))));
                s.Items.Add(new("AM service", OnOff(Str(status, "AMServiceEnabled"))));
                s.Items.Add(new("Network inspection (NIS)", OnOff(Str(status, "NISEnabled"))));
                var ver = Str(status, "AntivirusSignatureVersion");
                var when = WmiDate(Str(status, "AntivirusSignatureLastUpdated"));
                s.Items.Add(new("Signature version", ver == "—" ? "—" : $"{ver}  ({when})"));
                return s;
            }
        }
        catch { /* fall through to 3rd-party fallback */ }

        // Fallback: SecurityCenter2 (3rd-party AV, or Defender namespace unavailable).
        try
        {
            var products = All(@"root\SecurityCenter2", "AntiVirusProduct");
            if (products.Count > 0)
            {
                foreach (var p in products)
                {
                    var name = Str(p, "displayName");
                    var state = Str(p, "productState");
                    var stateTxt = int.TryParse(state, out var st) ? $"state 0x{st:X}" : "state —";
                    s.Items.Add(new(name, stateTxt));
                }
                return s;
            }
        }
        catch { /* fall through to empty */ }

        s.Items.Add(new("Antivirus", "—"));
        return s;
    }

    // ---- 2) Firewall ----
    private static InfoSection FirewallSection()
    {
        var s = new InfoSection { Title = "Firewall" };
        try
        {
            var profiles = All(@"root\StandardCimv2", "MSFT_NetFirewallProfile");
            if (profiles.Count > 0)
            {
                foreach (var p in profiles)
                    s.Items.Add(new(FirewallProfileName(Str(p, "Name")), Enabled(Str(p, "Enabled")) ? "On" : "Off"));
                return s;
            }
        }
        catch { /* fall through */ }

        s.Items.Add(new("Firewall", "—"));
        return s;
    }

    private static string FirewallProfileName(string raw) => raw switch
    {
        "1" => "Domain",
        "2" => "Private",
        "4" => "Public",
        "—" or "" => "Profile",
        _ => raw,
    };

    // ---- 3) BitLocker ----
    private static InfoSection BitLockerSection()
    {
        var s = new InfoSection { Title = "BitLocker" };
        try
        {
            var volumes = All(@"root\cimv2\Security\MicrosoftVolumeEncryption", "Win32_EncryptableVolume");
            if (volumes.Count > 0)
            {
                foreach (var v in volumes)
                {
                    var drive = Str(v, "DriveLetter");
                    var status = Str(v, "ProtectionStatus") switch
                    {
                        "0" => "Off",
                        "1" => "Protected",
                        "2" => "Unknown",
                        _ => "—",
                    };
                    s.Items.Add(new(drive == "—" ? "Volume" : drive, status));
                }
                return s;
            }
        }
        catch { /* fall through */ }

        s.Items.Add(new("BitLocker", "—"));
        return s;
    }

    // ---- 4) TPM ----
    private static InfoSection TpmSection()
    {
        var s = new InfoSection { Title = "TPM" };
        try
        {
            var tpm = First(@"root\cimv2\Security\MicrosoftTpm", "Win32_Tpm");
            if (tpm is not null)
            {
                s.Items.Add(new("Present", "Yes"));
                s.Items.Add(new("Version", Str(tpm, "SpecVersion")));
                s.Items.Add(new("Manufacturer", Str(tpm, "ManufacturerIdTxt")));
                s.Items.Add(new("Enabled", YesNo(Str(tpm, "IsEnabled_InitialValue"))));
                s.Items.Add(new("Activated", YesNo(Str(tpm, "IsActivated_InitialValue"))));
                return s;
            }
        }
        catch { /* fall through */ }

        s.Items.Add(new("TPM", "Not present"));
        return s;
    }

    // ---- 5) Secure Boot & UAC ----
    private static InfoSection SecureBootUacSection()
    {
        var s = new InfoSection { Title = "Secure Boot & UAC" };

        var secureBoot = RegDword(RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        s.Items.Add(new("Secure Boot", secureBoot switch { 1 => "On", 0 => "Off", _ => "—" }));

        var uac = RegDword(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
        s.Items.Add(new("UAC", uac switch { 1 => "On", 0 => "Off", _ => "—" }));

        return s;
    }

    // ---- WMI helpers ----
    private static List<ManagementBaseObject> All(string ns, string wmiClass)
    {
        try
        {
            var scope = new ManagementScope(ns);
            using var searcher = new ManagementObjectSearcher(scope, new SelectQuery($"SELECT * FROM {wmiClass}"));
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch { return new(); }
    }

    private static ManagementBaseObject? First(string ns, string wmiClass) => All(ns, wmiClass).FirstOrDefault();

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    // ---- Registry helper ----
    private static int? RegDword(RegistryHive hive, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            if (key?.GetValue(valueName) is { } raw && int.TryParse(raw.ToString(), out var v))
                return v;
            return null;
        }
        catch { return null; }
    }

    // ---- Value formatters ----
    private static bool Enabled(string s) =>
        s.Equals("True", StringComparison.OrdinalIgnoreCase) || s == "1";

    private static string OnOff(string s) => s == "—" ? "—" : (Enabled(s) ? "On" : "Off");

    private static string YesNo(string s) => s == "—" ? "—" : (Enabled(s) ? "Yes" : "No");

    private static string WmiDate(string wmi)
    {
        // WMI datetime: yyyymmddHHMMSS.ffffff+zzz — take the date portion.
        if (wmi.Length >= 8 && long.TryParse(wmi[..8], out _))
            return $"{wmi[..4]}-{wmi.Substring(4, 2)}-{wmi.Substring(6, 2)}";
        return wmi is "—" or "" ? "—" : wmi;
    }
}
