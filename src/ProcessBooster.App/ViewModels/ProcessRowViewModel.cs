using ProcessBooster.Core.Models;
using ProcessBooster.Core.Util;

namespace ProcessBooster.App.ViewModels;

/// <summary>One row in the live process table. Updated in place so selection/scroll survive refreshes.</summary>
public sealed class ProcessRowViewModel : ViewModelBase
{
    public int Pid { get; }

    private string _name = "";
    private string? _exePath;
    private string _cpu = "";
    private string _affinity = "";
    private string _io = "";
    private string _memory = "";
    private string _eco = "";
    private string _workingSet = "";
    private int _threads;
    private string? _rule;
    private double _cpuPercent;
    private ulong? _affinityRaw;

    public ProcessRowViewModel(ProcessSnapshot s, double cpuPercent = 0)
    {
        Pid = s.Pid;
        Update(s, cpuPercent);
    }

    /// <summary>Live CPU usage as a percentage of one core-second (0–100 across all cores).</summary>
    public double CpuPercent { get => _cpuPercent; private set => SetField(ref _cpuPercent, value); }

    public string Name { get => _name; private set => SetField(ref _name, value); }
    public string? ExePath { get => _exePath; set => SetField(ref _exePath, value); }
    public string Cpu { get => _cpu; private set => SetField(ref _cpu, value); }
    public string Affinity { get => _affinity; private set => SetField(ref _affinity, value); }
    public string Io { get => _io; private set => SetField(ref _io, value); }
    public string Memory { get => _memory; private set => SetField(ref _memory, value); }
    public string Eco { get => _eco; private set => SetField(ref _eco, value); }
    public string WorkingSet { get => _workingSet; private set => SetField(ref _workingSet, value); }
    public int Threads { get => _threads; private set => SetField(ref _threads, value); }
    public string? Rule { get => _rule; set => SetField(ref _rule, value); }

    /// <summary>Raw affinity bitmask (null/0 = all cores) — drives the affinity checkbox list.</summary>
    public ulong? AffinityMaskRaw { get => _affinityRaw; private set => SetField(ref _affinityRaw, value); }

    // Raw current values — let the right-click menus show a checkmark on what's applied now.
    public CpuPriority? CpuPriorityRaw { get; private set; }
    public IoPriority? IoRaw { get; private set; }
    public MemoryPriority? MemoryRaw { get; private set; }
    public bool? EcoRaw { get; private set; }
    public bool? BoostEnabledRaw { get; private set; }

    public void Update(ProcessSnapshot s, double cpuPercent)
    {
        Name = s.Name;
        CpuPercent = cpuPercent;
        if (s.ExePath is not null) ExePath = s.ExePath; // keep a path we resolved earlier
        Cpu = s.CpuPriority?.ToString() ?? "—";
        AffinityMaskRaw = s.AffinityMask;
        Affinity = s.AffinityMask is { } m && m != 0 ? AffinityMask.ToRangeString(m) : "all";
        Io = s.IoPriority?.ToString() ?? "—";
        Memory = s.MemoryPriority?.ToString() ?? "—";
        Eco = s.EfficiencyMode == true ? "On" : "—";
        CpuPriorityRaw = s.CpuPriority;
        IoRaw = s.IoPriority;
        MemoryRaw = s.MemoryPriority;
        EcoRaw = s.EfficiencyMode;
        BoostEnabledRaw = s.PriorityBoostEnabled;
        WorkingSet = FormatBytes(s.WorkingSetBytes);
        Threads = s.ThreadCount;
        Rule = s.GovernedByRule;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}
