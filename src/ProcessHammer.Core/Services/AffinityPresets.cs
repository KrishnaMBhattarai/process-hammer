namespace ProcessHammer.Core.Services;

/// <summary>
/// Pure helpers that turn a CPU topology into affinity bitmasks for the "All / P-cores / E-cores"
/// presets. Kept free of UI and OS calls so the logic is unit-testable.
/// </summary>
public static class AffinityPresets
{
    /// <summary>Bitmask covering logical processors 0..count-1 (all cores). count &gt;= 64 → all 64 bits.</summary>
    public static ulong AllMask(int count) =>
        count >= 64 ? ulong.MaxValue : count <= 0 ? 0UL : (1UL << count) - 1;

    /// <summary>
    /// Bitmask of the logical processors on performance (max EfficiencyClass) or efficiency
    /// (min EfficiencyClass) cores. Falls back to all cores when the topology is empty or the
    /// requested class yields nothing. Logical indices outside 0..63 are ignored (mask is 64-bit).
    /// </summary>
    public static ulong ClassMask(IReadOnlyList<CpuSetInfo> sets, bool performance, int count)
    {
        if (sets is null || sets.Count == 0) return AllMask(count);
        var target = performance ? sets.Max(s => s.EfficiencyClass) : sets.Min(s => s.EfficiencyClass);
        ulong mask = 0;
        foreach (var s in sets)
            if (s.EfficiencyClass == target && s.LogicalProcessorIndex is >= 0 and < 64)
                mask |= 1UL << s.LogicalProcessorIndex;
        return mask == 0 ? AllMask(count) : mask;
    }
}
