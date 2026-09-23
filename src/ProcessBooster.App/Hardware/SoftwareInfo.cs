using Microsoft.Win32;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Enumerates installed software from the registry Uninstall keys (64-bit, 32-bit and per-user
/// views). Every registry read is defensive: a missing hive, key or value is skipped rather than
/// throwing, so partial data still renders and <see cref="Collect"/> never fails.
/// </summary>
public static class SoftwareInfoService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallPathWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InfoSection> Collect()
    {
        var programs = new Dictionary<string, Program>(StringComparer.OrdinalIgnoreCase);

        try
        {
            ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, UninstallPath, programs);
            ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, UninstallPathWow, programs);
            ReadHive(RegistryHive.CurrentUser, RegistryView.Registry64, UninstallPath, programs);
        }
        catch { /* never throw */ }

        var sorted = programs.Values
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var section = new InfoSection { Title = $"Installed software ({sorted.Count})" };
        foreach (var p in sorted)
            section.Items.Add(new(p.DisplayName, FormatValue(p)));

        return new List<InfoSection> { section };
    }

    private sealed record Program(string DisplayName, string Version, string Publisher, string InstallDate);

    private static string FormatValue(Program p)
    {
        var value = $"{p.Version}  ·  {p.Publisher}";
        if (p.InstallDate is { Length: > 0 } && p.InstallDate != "—")
            value += $"  ·  {p.InstallDate}";
        return value;
    }

    private static void ReadHive(RegistryHive hive, RegistryView view, string path, Dictionary<string, Program> programs)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(path);
            if (root is null) return;

            foreach (var name in SafeSubKeyNames(root))
            {
                try
                {
                    using var sub = root.OpenSubKey(name);
                    if (sub is null) continue;
                    AddProgram(sub, programs);
                }
                catch { /* skip this entry */ }
            }
        }
        catch { /* skip this hive/view */ }
    }

    private static void AddProgram(RegistryKey sub, Dictionary<string, Program> programs)
    {
        if (Int(sub, "SystemComponent") == 1) return;

        var displayName = Get(sub, "DisplayName");
        if (displayName is null or "") return;
        if (IsWindowsUpdate(displayName)) return;

        var version = Get(sub, "DisplayVersion") is { Length: > 0 } v ? v : "—";
        var publisher = Get(sub, "Publisher") is { Length: > 0 } pub ? pub : "—";
        var installDate = DecodeDate(Get(sub, "InstallDate"));

        // De-duplicate by DisplayName (case-insensitive); first (64-bit) view wins.
        if (!programs.ContainsKey(displayName))
            programs[displayName] = new Program(displayName, version, publisher, installDate);
    }

    private static bool IsWindowsUpdate(string name) =>
        name.StartsWith("KB", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Update for", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase);

    // ---- registry helpers ----
    private static string[] SafeSubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch { return Array.Empty<string>(); }
    }

    private static string? Get(RegistryKey key, string name)
    {
        try { return key.GetValue(name)?.ToString()?.Trim(); }
        catch { return null; }
    }

    private static int Int(RegistryKey key, string name)
    {
        try
        {
            var raw = key.GetValue(name);
            return raw is null ? 0 : (int.TryParse(raw.ToString(), out var i) ? i : 0);
        }
        catch { return 0; }
    }

    private static string DecodeDate(string? raw)
    {
        // InstallDate is stored as yyyymmdd -> render as yyyy-MM-dd.
        if (raw is { Length: 8 } && long.TryParse(raw, out _))
            return $"{raw[..4]}-{raw.Substring(4, 2)}-{raw.Substring(6, 2)}";
        return "—";
    }
}
