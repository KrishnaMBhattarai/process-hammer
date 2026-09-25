using System.IO;
using System.Management;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// Enumerates programs that run at logon/startup. Sources are WMI Win32_StartupCommand plus the
/// per-user and common Startup folders. Every query/IO call is defensive: failures yield no rows
/// rather than throwing, and <see cref="CollectTables"/> never throws so partial data still renders.
/// </summary>
public static class StartupInfoService
{
    public static List<DataTable> CollectTables()
    {
        try
        {
            // De-duplicate by Name + Command so a WMI entry and its Startup-folder shortcut don't double up.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var rows = new List<string[]>();
            rows.AddRange(WmiRows(seen));
            rows.AddRange(FolderRows(seen));

            rows.Sort((a, b) => string.Compare(a[0], b[0], StringComparison.OrdinalIgnoreCase));

            var table = new DataTable(
                $"Startup programs ({rows.Count})",
                new[] { "Name", "Command", "Location" },
                rows);

            return new List<DataTable> { table };
        }
        catch
        {
            return new List<DataTable>();
        }
    }

    // ---- Win32_StartupCommand: one row per entry ----
    private static List<string[]> WmiRows(HashSet<string> seen)
    {
        var result = new List<string[]>();
        foreach (var mo in All("Win32_StartupCommand"))
        {
            var name = Str(mo, "Name");
            var command = Str(mo, "Command");
            var location = Str(mo, "Location");

            if (!seen.Add($"{name}{command}"))
                continue;

            result.Add(new[] { name, command, location });
        }
        return result;
    }

    // ---- Startup folders (per-user + all-users): one row per .lnk/.exe file ----
    private static List<string[]> FolderRows(HashSet<string> seen)
    {
        var result = new List<string[]>();
        foreach (var folder in StartupFolders())
        {
            foreach (var file in Files(folder))
            {
                var name = SafeFileName(file);
                if (!seen.Add($"{name}{file}"))
                    continue;

                result.Add(new[] { name, file, "Startup folder" });
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
