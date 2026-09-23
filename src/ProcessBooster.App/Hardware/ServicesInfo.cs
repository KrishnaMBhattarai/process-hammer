using System.Management;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects the Windows service inventory via WMI (Win32_Service). Every query is defensive: a
/// missing class or property yields "—" rather than throwing, so partial data still renders and
/// <see cref="Collect"/> never propagates an exception.
/// </summary>
public static class ServicesInfoService
{
    public static List<InfoSection> Collect()
    {
        try
        {
            var services = All("Win32_Service");

            var running = services
                .Where(s => string.Equals(Str(s, "State"), "Running", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => Str(s, "DisplayName"), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var stopped = services
                .Where(s => !string.Equals(Str(s, "State"), "Running", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => Str(s, "DisplayName"), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var summary = new InfoSection { Title = "Services" };
            summary.Items.Add(new("Total", services.Count.ToString()));
            summary.Items.Add(new("Running", running.Count.ToString()));
            summary.Items.Add(new("Stopped", stopped.Count.ToString()));

            var runningSection = new InfoSection { Title = $"Running services ({running.Count})" };
            foreach (var s in running)
                runningSection.Items.Add(new(Str(s, "DisplayName"), $"{Str(s, "StartMode")} · {Str(s, "StartName")}"));

            var stoppedSection = new InfoSection { Title = $"Stopped services ({stopped.Count})" };
            foreach (var s in stopped)
                stoppedSection.Items.Add(new(Str(s, "DisplayName"), $"{Str(s, "State")} · {Str(s, "StartMode")}"));

            return new List<InfoSection> { summary, runningSection, stoppedSection };
        }
        catch
        {
            return new List<InfoSection>();
        }
    }

    // ---- WMI helpers ----
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
