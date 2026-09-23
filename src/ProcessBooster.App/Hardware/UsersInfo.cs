using System.Management;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects a user-account overview: the current session, every local user account, and the
/// members of the local Administrators group. Every WMI/Environment/registry call is defensive,
/// so <see cref="Collect"/> never throws and partial data still renders ("—" for missing values).
/// </summary>
public static class UsersInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection> { CurrentSession() };
        sections.AddRange(LocalAccounts());
        sections.Add(Administrators());
        return sections;
    }

    private static InfoSection CurrentSession()
    {
        var cs = First("Win32_ComputerSystem");
        var s = new InfoSection { Title = "Current session" };
        s.Items.Add(new("User", Env(() => Environment.UserName)));
        s.Items.Add(new("Domain", Env(() => Environment.UserDomainName)));
        s.Items.Add(new("Running as admin", Env(() =>
            new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? "Yes" : "No")));
        s.Items.Add(new("SID", Env(() => WindowsIdentity.GetCurrent().User?.Value)));
        s.Items.Add(new("Profile folder", Env(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
        s.Items.Add(new("Console user", Str(cs, "UserName")));
        return s;
    }

    private static List<InfoSection> LocalAccounts()
    {
        var sections = new List<InfoSection>();
        foreach (var u in All("SELECT * FROM Win32_UserAccount WHERE LocalAccount=True"))
        {
            var name = Str(u, "Name");
            if (name == "—") continue; // trivially empty — skip noise

            var s = new InfoSection { Title = name };
            Add(s, "Full name", Str(u, "FullName"));
            Add(s, "Status", Bool(u, "Disabled") == "Yes" ? "Disabled" : "Enabled");
            Add(s, "Locked out", Bool(u, "Lockout"));
            Add(s, "Password required", Bool(u, "PasswordRequired"));
            Add(s, "Password expires", Bool(u, "PasswordExpires"));
            Add(s, "SID", Str(u, "SID"));
            Add(s, "Description", Str(u, "Description"));
            sections.Add(s);
        }
        return sections;
    }

    private static InfoSection Administrators()
    {
        var s = new InfoSection { Title = "Administrators" };
        foreach (var gu in All("SELECT * FROM Win32_GroupUser"))
        {
            var group = Str(gu, "GroupComponent");
            if (!group.Contains("Name=\"Administrators\"")) continue;

            var part = Str(gu, "PartComponent");
            var domain = Extract(part, "Domain");
            var member = Extract(part, "Name");
            if (member == "—") continue;

            var value = domain == "—" ? member : $"{domain}\\{member}";
            s.Items.Add(new("Member", value));
        }
        if (s.Items.Count == 0)
            s.Items.Add(new("Administrators", "—"));
        return s;
    }

    // ---- helpers ----
    private static void Add(InfoSection s, string label, string value)
    {
        if (value != "—") s.Items.Add(new(label, value));
    }

    private static List<ManagementBaseObject> All(string query)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch { return new(); }
    }

    private static ManagementBaseObject? First(string wmiClass) => All($"SELECT * FROM {wmiClass}").FirstOrDefault();

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    /// <summary>Decodes a WMI boolean property to "Yes"/"No", or "—" if absent.</summary>
    private static string Bool(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop] is bool b ? (b ? "Yes" : "No") : "—"; }
        catch { return "—"; }
    }

    /// <summary>Wraps an Environment/identity getter so a failure yields "—" instead of throwing.</summary>
    private static string Env(Func<string?> get)
    {
        try { return get()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    /// <summary>Pulls a named field out of a WMI reference string, e.g. Domain="PC",Name="Krishna".</summary>
    private static string Extract(string reference, string key)
    {
        try
        {
            var m = Regex.Match(reference, key + "=\"([^\"]+)\"");
            return m.Success && m.Groups[1].Value.Trim() is { Length: > 0 } v ? v : "—";
        }
        catch { return "—"; }
    }
}
