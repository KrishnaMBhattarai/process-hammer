namespace ProcessBooster.Core.Models;

/// <summary>
/// A read-only view of one process at a moment in time, for the live table.
/// Fields that couldn't be read (access denied, protected process) are left null.
/// </summary>
public sealed class ProcessSnapshot
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string? ExePath { get; init; }

    public CpuPriority? CpuPriority { get; init; }
    public ulong? AffinityMask { get; init; }
    public int AffinityCoreCount { get; init; }
    public IoPriority? IoPriority { get; init; }
    public MemoryPriority? MemoryPriority { get; init; }
    public bool? EfficiencyMode { get; init; }
    public bool? PriorityBoostEnabled { get; init; }

    public long WorkingSetBytes { get; init; }
    public int ThreadCount { get; init; }

    /// <summary>Total CPU time consumed so far; the UI derives live CPU % from deltas between refreshes.</summary>
    public TimeSpan CpuTime { get; init; }

    /// <summary>Name of the rule currently governing this process, if any (set by the engine).</summary>
    public string? GovernedByRule { get; set; }
}

/// <summary>Result of applying one action, for logging and the UI.</summary>
public readonly record struct ActionResult(string Action, ActionStatus Status, string? Detail = null)
{
    public override string ToString() =>
        Detail is null ? $"{Action}: {Status}" : $"{Action}: {Status} ({Detail})";
}
