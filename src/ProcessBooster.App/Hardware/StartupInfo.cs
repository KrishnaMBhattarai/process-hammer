using System.IO;
using System.Management;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Enumerates programs that run at logon/startup. Sources are WMI Win32_StartupCommand plus the
/// per-user and common Startup folders. Every query/IO call is defensive: failures yield no rows
/// rather than throwing, and <see cref="Collect"/> never throws so partial data still renders.
/// </summary>
public static class StartupInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        // De-duplicate by Name + Command so a WMI entry and its Startup-folder shortcut don't double up.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entries = new List<InfoSection>();
        entries.AddRange(WmiEntries(seen));
        entries.AddRange(FolderEntries(seen));

        var summary = new InfoSection { Title = "Startup" };
        summary.Items.Add(new("Entries", entries.Count.ToString()));
        sections.Add(summary);
        sections.AddRange(entries);
        return sections;
    }

    // ---- Win32_StartupCommand: one section per entry, titled by Name ----
    private static List<InfoSection> WmiEntries(HashSet<string> seen)
    {
        var result = new List<InfoSection>();
        foreach (var mo in All("Win32_StartupCommand"))
        {
            var name = Str(mo, "Name");
            var command = Str(mo, "Command");
            var location = Str(mo, "Location");
            var user = Str(mo, "User");

            if (!seen.Add($"{name}{command}"))
                continue;

            var s = new InfoSection { Title = name == "—" ? command : name };
            AddIf(s, "Command", command);
            AddIf(s, "Location", location);
            AddIf(s, "User", user);
            result.Add(s);
        }
        return result;
    }

    // ---- Startup folders (per-user + all-users): list .lnk/.exe as sections ----
    private static List<InfoSection> FolderEntries(HashSet<string> seen)
    {
        var result = new List<InfoSection>();
        foreach (var folder in StartupFolders())
        {
            foreach (var file in Files(folder))
            {
                var name = SafeFileName(file);
                if (!seen.Add($"{name}{file}"))
                    continue;

                var s = new InfoSection { Title = name };
                AddIf(s, "Command", file);
                AddIf(s, "Location", "Startup folder");
                result.Add(s);
            }
        }
        return result;
    }

    private static List<string> StartupFolders()
    {
        var folders = new List<string>();
        SafeAddFolder(folders, Environment.SpecialFolder.Startup);
        SafeAddFolder(folders, Environment.SpecialFolder.CommonStartup);
        return folders;
    }

    private static void SafeAddFolder(List<string> folders, Environment.SpecialFolder which)
    {
        try
        {
            var path = Environment.GetFolderPath(which);
            if (path is { Length: > 0 })
                folders.Add(path);
        }
        catch { /* ignore */ }
    }

    private static List<string> Files(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return new();
            return Directory.EnumerateFiles(folder)
                .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch { return new(); }
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path) is { Length: > 0 } n ? n : path; }
        catch { return path; }
    }

    // ---- helpers ----
    private static void AddIf(InfoSection s, string label, string value)
    {
        if (value != "—")
            s.Items.Add(new(label, value));
    }

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
}
