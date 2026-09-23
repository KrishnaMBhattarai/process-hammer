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
}
