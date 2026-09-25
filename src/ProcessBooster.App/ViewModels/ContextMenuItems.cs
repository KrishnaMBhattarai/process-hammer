using ProcessBooster.Core.Models;
using ProcessBooster.Core.Util;

namespace ProcessBooster.App.ViewModels;

/// <summary>One logical CPU in the right-click "CPU affinity" checkbox list. Toggling applies immediately.</summary>
public sealed class AffinityCoreItem : ViewModelBase
{
    private readonly Action _onToggle;
    private bool _checked;

    public AffinityCoreItem(int index, Action onToggle)
    {
        Index = index;
        _onToggle = onToggle;
    }

    public int Index { get; }
    public string Label => $"CPU {Index}";

    public bool IsChecked
    {
        get => _checked;
        set { if (SetField(ref _checked, value)) _onToggle(); }
    }
}

/// <summary>One Windows power plan in the right-click "Power profile" list. <see cref="SelectCommand"/> switches to it.</summary>
public sealed class PowerProfileItem : ViewModelBase
{
    private bool _active;

    public PowerProfileItem(Guid guid, string name)
    {
        Guid = guid;
        Name = name;
    }

    public Guid Guid { get; }
    public string Name { get; }
    public bool IsActive { get => _active; set => SetField(ref _active, value); }
    public RelayCommand? SelectCommand { get; set; }
}

/// <summary>One row in the Booster Rules tab: a saved rule summarised for display.</summary>
public sealed class RuleRowViewModel : ViewModelBase
{
    private bool _running;

    public RuleRowViewModel(ProcessRule rule) => Rule = rule;

    public ProcessRule Rule { get; }

    public string Process => Rule.Match;
    public string EnabledText => Rule.Enabled ? "Yes" : "No";

    public bool Running { get => _running; set { if (SetField(ref _running, value)) Raise(nameof(RunningText)); } }
    public string RunningText => _running ? "Running" : "Not running";

    public string Cpu => Rule.CpuPriority?.ToString() ?? "—";
    public string Affinity => Rule.AffinityMask is { } m && m != 0 ? AffinityMask.ToRangeString(m) : "—";
    public string Io => Rule.IoPriority?.ToString() ?? "—";
    public string Memory => Rule.MemoryPriority?.ToString() ?? "—";
    public string Eco => Rule.EfficiencyMode is { } e ? (e ? "On" : "Off") : "—";
    public string Boost => Rule.DisablePriorityBoost is { } b ? (b ? "Disabled" : "Enabled") : "—";
    public string CpuSets => Rule.CpuSetSelection == CpuSetSelection.Unset ? "—" : Rule.CpuSetSelection.ToString();
    public string GpuPref => Rule.GpuPreference?.ToString() ?? "—";
    public string GpuSched => Rule.GpuSchedulingPriority?.ToString() ?? "—";
    public string PowerPlan => string.IsNullOrWhiteSpace(Rule.PowerPlan) ? "—" : Rule.PowerPlan!;
    public string Note => Rule.Note ?? "";
}
