using System.Text;

namespace ProcessBooster.App.Hardware;

/// <summary>Aggregates every collector into one plain-text system report (for Copy / Export).</summary>
public static class ReportBuilder
{
    public static string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine("PROCESS BOOSTER — SYSTEM REPORT");
        sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();

        Add(sb, "SYSTEM", Safe(SystemInfoService.Collect));
        Add(sb, "OPERATING SYSTEM", Safe(OsInfoService.Collect));
        Add(sb, "SECURITY", Safe(SecurityInfoService.Collect));
        Add(sb, "USERS", Safe(UsersInfoService.Collect));
        AddTables(sb, "STARTUP", SafeT(StartupInfoService.CollectTables));
        AddTables(sb, "INSTALLED SOFTWARE", SafeT(SoftwareInfoService.CollectTables));
        AddTables(sb, "SERVICES", SafeT(ServicesInfoService.CollectTables));
        Add(sb, "DEVICES", Safe(DevicesInfoService.Collect));
        AddTables(sb, "ENVIRONMENT", SafeT(EnvironmentInfoService.CollectTables));
        Add(sb, "CPU", Safe(CpuInfoService.Collect));
        Add(sb, "MEMORY", Safe(MemoryInfoService.Collect));
        Add(sb, "GRAPHICS", Safe(GraphicsInfoService.Collect));
        Add(sb, "DISPLAY", Safe(DisplayInfoService.Collect));
        Add(sb, "STORAGE", Safe(StorageInfoService.Collect));
        Add(sb, "NETWORK", Safe(NetworkInfoService.Collect));
        Add(sb, "POWER", Safe(PowerInfoService.Collect));

        return sb.ToString();
    }

    private static List<InfoSection> Safe(Func<List<InfoSection>> collect)
    {
        try { return collect(); } catch { return new List<InfoSection>(); }
    }

    private static void Add(StringBuilder sb, string tab, List<InfoSection> sections)
    {
        sb.AppendLine("==================  " + tab + "  ==================");
        foreach (var s in sections)
        {
            sb.AppendLine("  [" + s.Title + "]");
            foreach (var it in s.Items)
                sb.AppendLine($"    {it.Label,-24} {it.Value}");
        }
        sb.AppendLine();
    }

    private static List<DataTable> SafeT(Func<List<DataTable>> collect)
    {
        try { return collect(); } catch { return new List<DataTable>(); }
    }

    private static void AddTables(StringBuilder sb, string tab, List<DataTable> tables)
    {
        sb.AppendLine("==================  " + tab + "  ==================");
        foreach (var t in tables)
        {
            sb.AppendLine("  [" + t.Title + "]");
            sb.AppendLine("    " + string.Join("  |  ", t.Columns));
            foreach (var row in t.Rows)
                sb.AppendLine("    " + string.Join("  |  ", row));
        }
        sb.AppendLine();
    }
}
