namespace ProcessBooster.Core.Models;

/// <summary>
/// A persistent rule: "when a process matching <see cref="Match"/> is running, apply these settings".
/// Every action is nullable — only the ones that are set are applied, so a rule can be as narrow
/// (just CPU priority) or as broad (everything) as the user wants.
/// </summary>
public sealed class ProcessRule
{
    /// <summary>Process name to match, with or without ".exe" (case-insensitive). E.g. "MyGame".</summary>
    public string Match { get; set; } = "";

    /// <summary>Human note shown in the UI (optional).</summary>
    public string? Note { get; set; }

    /// <summary>Rule is considered while enabled; disabled rules are ignored by the engine.</summary>
    public bool Enabled { get; set; } = true;

    // ---- actions (null = leave alone) ----
    public CpuPriority? CpuPriority { get; set; }

    /// <summary>CPU affinity bitmask; null or 0 = all cores. Bit i = logical processor i.</summary>
    public ulong? AffinityMask { get; set; }

    public IoPriority? IoPriority { get; set; }
    public MemoryPriority? MemoryPriority { get; set; }

    /// <summary>EcoQoS efficiency mode. true = enable, false = force-disable, null = leave system-managed.</summary>
    public bool? EfficiencyMode { get; set; }

    /// <summary>Disable the priority boost the scheduler normally gives on foreground/UI events. null = leave.</summary>
    public bool? DisablePriorityBoost { get; set; }

    public GpuPreference? GpuPreference { get; set; }
    public GpuSchedulingPriority? GpuSchedulingPriority { get; set; }

    public CpuSetSelection CpuSetSelection { get; set; } = CpuSetSelection.Unset;

    /// <summary>Explicit CPU-set IDs used when <see cref="CpuSetSelection"/> is Custom.</summary>
    public List<uint> CpuSetIds { get; set; } = new();

    /// <summary>Power plan (by name or GUID) to switch to while any rule process is running. Optional.</summary>
    public string? PowerPlan { get; set; }

    /// <summary>Returns true if any action is set (the rule would do something).</summary>
    public bool HasAnyAction =>
        CpuPriority is not null || (AffinityMask is not null && AffinityMask != 0) ||
        IoPriority is not null || MemoryPriority is not null || EfficiencyMode is not null ||
        DisablePriorityBoost is not null || GpuPreference is not null ||
        GpuSchedulingPriority is not null || CpuSetSelection != CpuSetSelection.Unset ||
        !string.IsNullOrWhiteSpace(PowerPlan);

    /// <summary>The bare process name without extension, lower-cased, for matching.</summary>
    public string NormalizedMatch => Normalize(Match);

    /// <summary>Normalizes "Foo.EXE", "foo", " Foo " → "foo".</summary>
    public static string Normalize(string name)
    {
        var n = (name ?? "").Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = n[..^4];
        return n.ToLowerInvariant();
    }
}
