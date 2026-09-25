using System.Management;

namespace ProcessHammer.App.Hardware;

/// <summary>
/// Collects the Windows service inventory via WMI (Win32_Service). Every query is defensive: a
/// missing class or property yields "—" rather than throwing, so partial data still renders and
/// <see cref="CollectTables"/> never propagates an exception.
/// </summary>
public static class ServicesInfoService
{
    public static List<DataTable> CollectTables()
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

            var runningRows = running
                .Select(s => new[] { Str(s, "DisplayName"), Str(s, "StartMode"), Str(s, "StartName") })
                .ToList();

            var stoppedRows = stopped
                .Select(s => new[] { Str(s, "DisplayName"), Str(s, "State"), Str(s, "StartMode") })
                .ToList();

            var runningTable = new DataTable(
                $"Running ({running.Count})",
                new[] { "Service", "Start type", "Account" },
                runningRows);

            var stoppedTable = new DataTable(
                $"Stopped ({stopped.Count})",
                new[] { "Service", "State", "Start type" },
                stoppedRows);

            return new List<DataTable> { runningTable, stoppedTable };
        }
        catch
        {
            return new List<DataTable>();
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
