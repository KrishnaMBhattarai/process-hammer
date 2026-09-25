namespace ProcessHammer.Core.Models;

/// <summary>Windows process priority class (kernel32 SetPriorityClass).</summary>
public enum CpuPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    Realtime,
}

/// <summary>
/// I/O priority hint (NtSetInformationProcess / ProcessIoPriority).
/// Only VeryLow, Low and Normal are settable from user mode; High/Critical are system-reserved.
/// </summary>
public enum IoPriority
{
    VeryLow = 0,
    Low = 1,
    Normal = 2,
}

/// <summary>Working-set memory priority (MEMORY_PRIORITY_INFORMATION).</summary>
public enum MemoryPriority
{
    VeryLow = 1,
    Low = 2,
    Medium = 3,
    BelowNormal = 4,
    Normal = 5,
}

/// <summary>Per-app GPU preference (HKCU DirectX\UserGpuPreferences). Persistent, applies on relaunch.</summary>
public enum GpuPreference
{
    SystemDefault = 0,
    PowerSaving = 1,
    HighPerformance = 2,
}

/// <summary>GPU scheduling priority class (D3DKMTSetProcessSchedulingPriorityClass).</summary>
public enum GpuSchedulingPriority
{
    Idle = 0,
    BelowNormal = 1,
    Normal = 2,
    AboveNormal = 3,
    High = 4,
    Realtime = 5,
}

/// <summary>How a rule chooses which CPU cores (as CPU Sets) a process may run on.</summary>
public enum CpuSetSelection
{
    /// <summary>Do not touch CPU sets.</summary>
    Unset,
    /// <summary>Clear any CPU-set restriction (all cores).</summary>
    All,
    /// <summary>Performance cores only (highest EfficiencyClass).</summary>
    PerformanceCores,
    /// <summary>Efficiency cores only (lowest EfficiencyClass).</summary>
    EfficiencyCores,
    /// <summary>Use the explicit <see cref="ProcessRule.CpuSetIds"/> list.</summary>
    Custom,
}

/// <summary>Outcome of applying a single action to a process.</summary>
public enum ActionStatus
{
    Applied,
    AlreadySet,
    Skipped,
    Failed,
    Unsupported,
}
