using System.Collections.ObjectModel;
using ProcessHammer.Core.Models;
using ProcessHammer.Core.Services;
using ProcessHammer.Core.Util;

namespace ProcessHammer.App.ViewModels;

/// <summary>Backs the right-hand editor: builds/reads a <see cref="ProcessRule"/> for the selected process.</summary>
public sealed class RuleEditorViewModel : ViewModelBase
{
    private readonly CpuTopology _topology;

    public RuleEditorViewModel(CpuTopology topology)
    {
        _topology = topology;

        CpuPriorityOptions = Build("Leave unchanged", Enum.GetValues<CpuPriority>().Cast<object>());
        IoOptions = Build("Leave unchanged", Enum.GetValues<IoPriority>().Cast<object>());
        MemoryOptions = Build("Leave unchanged", Enum.GetValues<MemoryPriority>().Cast<object>());
        GpuPreferenceOptions = Build("Leave unchanged", Enum.GetValues<GpuPreference>().Cast<object>());
        GpuSchedulingOptions = Build("Leave unchanged", Enum.GetValues<GpuSchedulingPriority>().Cast<object>());
        EfficiencyOptions = new() { new("Leave unchanged", null), new("On (EcoQoS)", true), new("Off (system-managed)", false) };
        BoostOptions = new() { new("Leave unchanged", null), new("Boost enabled", false), new("Boost disabled", true) };
        CpuSetOptions = new()
        {
            new("Leave unchanged", CpuSetSelection.Unset),
            new("All cores", CpuSetSelection.All),
            new("Performance cores", CpuSetSelection.PerformanceCores),
            new("Efficiency cores", CpuSetSelection.EfficiencyCores),
            new("Custom (core list)", CpuSetSelection.Custom),
        };

        for (var i = 0; i < Math.Max(1, Environment.ProcessorCount); i++)
        {
            var index = i;
            AffinityCores.Add(new AffinityCoreItem(index, OnAffinityCoreToggled));
        }
        AffinityPresetCommand = new RelayCommand(SetAffinityPreset);

        LoadFrom(null); // start empty
    }

    // ---- CPU affinity as per-core checkboxes (keeps AffinityText, the source of truth, in sync) ----
    public ObservableCollection<AffinityCoreItem> AffinityCores { get; } = new();
    public RelayCommand AffinityPresetCommand { get; }
    private bool _suppressAffinitySync;

    private void OnAffinityCoreToggled()
    {
        if (_suppressAffinitySync) return;
        RecomputeAffinityText();
    }

    private void RecomputeAffinityText()
    {
        ulong mask = 0;
        foreach (var c in AffinityCores) if (c.IsChecked) mask |= 1UL << c.Index;
        AffinityText = mask == 0 || mask == AllMask() ? "" : AffinityMask.ToRangeString(mask);
    }

    private void SyncAffinityCoresFromText()
    {
        _suppressAffinitySync = true;
        try
        {
            if (!AffinityMask.TryParseRangeString(AffinityText, 64, out var mask) || mask == 0)
                foreach (var c in AffinityCores) c.IsChecked = true; // blank = all cores
            else
                foreach (var c in AffinityCores) c.IsChecked = (mask & (1UL << c.Index)) != 0;
        }
        finally { _suppressAffinitySync = false; }
    }

    private void SetAffinityPreset(object? param)
    {
        var mask = param?.ToString() switch
        {
            "p" => CoreClassMask(performance: true),
            "e" => CoreClassMask(performance: false),
            _ => AllMask(),
        };
        _suppressAffinitySync = true;
        try { foreach (var c in AffinityCores) c.IsChecked = (mask & (1UL << c.Index)) != 0; }
        finally { _suppressAffinitySync = false; }
        RecomputeAffinityText();
    }

    private ulong AllMask() => AffinityPresets.AllMask(AffinityCores.Count);

    private ulong CoreClassMask(bool performance) => AffinityPresets.ClassMask(_topology.Sets, performance, AffinityCores.Count);

    // ---- target process ----
    private string _targetName = "";
    private int _targetPid;
    private string? _targetExePath;
    private bool _hasTarget;

    public string TargetName { get => _targetName; private set => SetField(ref _targetName, value); }
    public int TargetPid { get => _targetPid; private set => SetField(ref _targetPid, value); }
    public string? TargetExePath { get => _targetExePath; private set => SetField(ref _targetExePath, value); }
    public bool HasTarget { get => _hasTarget; private set => SetField(ref _hasTarget, value); }
    public string TargetHeader => HasTarget ? $"{TargetName}  ·  PID {TargetPid}" : "Select a process";

    // ---- option lists ----
    public List<OptionItem> CpuPriorityOptions { get; }
    public List<OptionItem> IoOptions { get; }
    public List<OptionItem> MemoryOptions { get; }
    public List<OptionItem> GpuPreferenceOptions { get; }
    public List<OptionItem> GpuSchedulingOptions { get; }
    public List<OptionItem> EfficiencyOptions { get; }
    public List<OptionItem> BoostOptions { get; }
    public List<OptionItem> CpuSetOptions { get; }

    // ---- live change signal (a user tweak here applies immediately + mirrors to the menu) ----
    public enum EditorSetting { CpuPriority, Io, Memory, Efficiency, Boost, CpuSet, GpuPreference, GpuScheduling, Affinity }
    public event Action<EditorSetting>? SettingChangedByUser;
    private bool _loading;
    private void Fire(EditorSetting s) { if (!_loading) SettingChangedByUser?.Invoke(s); }

    /// <summary>Set-ids for the current custom core list (exposed so the host can apply CPU sets live).</summary>
    public IReadOnlyList<uint> CustomCpuSetIds() => SetIdsFromIndices(CustomCpuSetText);

    // ---- selected values ----
    private OptionItem? _cpu, _io, _mem, _gpuPref, _gpuSched, _eco, _boost, _cpuSet;
    private string _affinityText = "";
    private string _customCpuSetText = "";

    public OptionItem? SelectedCpuPriority { get => _cpu; set { if (SetField(ref _cpu, value)) Fire(EditorSetting.CpuPriority); } }
    public OptionItem? SelectedIo { get => _io; set { if (SetField(ref _io, value)) Fire(EditorSetting.Io); } }
    public OptionItem? SelectedMemory { get => _mem; set { if (SetField(ref _mem, value)) Fire(EditorSetting.Memory); } }
    public OptionItem? SelectedGpuPreference { get => _gpuPref; set { if (SetField(ref _gpuPref, value)) Fire(EditorSetting.GpuPreference); } }
    public OptionItem? SelectedGpuScheduling { get => _gpuSched; set { if (SetField(ref _gpuSched, value)) Fire(EditorSetting.GpuScheduling); } }
    public OptionItem? SelectedEfficiency { get => _eco; set { if (SetField(ref _eco, value)) Fire(EditorSetting.Efficiency); } }
    public OptionItem? SelectedBoost { get => _boost; set { if (SetField(ref _boost, value)) Fire(EditorSetting.Boost); } }

    public OptionItem? SelectedCpuSet
    {
        get => _cpuSet;
        set { if (SetField(ref _cpuSet, value)) { Raise(nameof(IsCustomCpuSet)); Fire(EditorSetting.CpuSet); } }
    }

    public bool IsCustomCpuSet => (CpuSetSelection?)SelectedCpuSet?.Value == CpuSetSelection.Custom;

    public string AffinityText { get => _affinityText; set { if (SetField(ref _affinityText, value)) Fire(EditorSetting.Affinity); } }
    public string CustomCpuSetText { get => _customCpuSetText; set { if (SetField(ref _customCpuSetText, value)) Fire(EditorSetting.CpuSet); } }

    public void SetTarget(string name, int pid, string? exePath)
    {
        TargetName = name;
        TargetPid = pid;
        TargetExePath = exePath;
        HasTarget = true;
        Raise(nameof(TargetHeader));
    }

    public void Clear()
    {
        HasTarget = false;
        TargetName = ""; TargetPid = 0; TargetExePath = null;
        Raise(nameof(TargetHeader));
        LoadFrom(null);
    }

    /// <summary>
    /// Populate the editor. A saved <paramref name="rule"/> takes precedence; for anything it doesn't
    /// set, fall back to the process's <paramref name="current"/> live state so the dropdowns show what
    /// is actually applied now (rather than "Leave unchanged"). This mirrors the right-click menu.
    /// </summary>
    public void LoadFrom(ProcessRule? rule, CurrentState? current = null)
    {
        _loading = true; // programmatic fill must not trigger a live re-apply
        try
        {
            var c = current ?? default;
            var boostDisabled = rule?.DisablePriorityBoost ?? (c.BoostEnabled is { } en ? !en : (bool?)null);

            var cpuSet = rule is { } r && r.CpuSetSelection != CpuSetSelection.Unset
                ? r.CpuSetSelection : c.CpuSets ?? CpuSetSelection.Unset;

            SelectedCpuPriority = Match(CpuPriorityOptions, rule?.CpuPriority ?? c.Cpu);
            SelectedIo = Match(IoOptions, rule?.IoPriority ?? c.Io);
            SelectedMemory = Match(MemoryOptions, rule?.MemoryPriority ?? c.Memory);
            SelectedGpuPreference = Match(GpuPreferenceOptions, rule?.GpuPreference ?? c.GpuPreference);
            SelectedGpuScheduling = Match(GpuSchedulingOptions, rule?.GpuSchedulingPriority ?? c.GpuScheduling);
            SelectedEfficiency = Match(EfficiencyOptions, rule?.EfficiencyMode ?? c.Eco);
            SelectedBoost = Match(BoostOptions, boostDisabled);
            SelectedCpuSet = Match(CpuSetOptions, cpuSet) ?? CpuSetOptions[0];

            var affinity = rule?.AffinityMask is { } m && m != 0 ? m
                : c.Affinity is { } am && am != 0 ? am : (ulong?)null;
            AffinityText = NormalizeAffinity(affinity);
            CustomCpuSetText = rule is { CpuSetSelection: CpuSetSelection.Custom } ? IndicesFromSetIds(rule.CpuSetIds) : "";
            SyncAffinityCoresFromText();
        }
        finally { _loading = false; }
    }

    /// <summary>All-cores (or empty) masks render as blank; a real subset renders as a range string.</summary>
    private string NormalizeAffinity(ulong? mask) =>
        mask is not { } m || m == 0 || m == AllMask() ? "" : AffinityMask.ToRangeString(m);

    /// <summary>Builds a rule from the current editor state. Returns null if nothing is set.</summary>
    public ProcessRule? BuildRule()
    {
        if (!HasTarget) return null;
        var rule = new ProcessRule { Match = TargetName, Enabled = true };

        rule.CpuPriority = (CpuPriority?)SelectedCpuPriority?.Value;
        rule.IoPriority = (IoPriority?)SelectedIo?.Value;
        rule.MemoryPriority = (MemoryPriority?)SelectedMemory?.Value;
        rule.GpuPreference = (GpuPreference?)SelectedGpuPreference?.Value;
        rule.GpuSchedulingPriority = (GpuSchedulingPriority?)SelectedGpuScheduling?.Value;
        rule.EfficiencyMode = (bool?)SelectedEfficiency?.Value;
        rule.DisablePriorityBoost = (bool?)SelectedBoost?.Value;

        if (AffinityMask.TryParseRangeString(AffinityText, 64, out var mask) && mask != 0)
            rule.AffinityMask = mask;

        var sel = (CpuSetSelection?)SelectedCpuSet?.Value ?? CpuSetSelection.Unset;
        rule.CpuSetSelection = sel;
        if (sel == CpuSetSelection.Custom)
            rule.CpuSetIds = SetIdsFromIndices(CustomCpuSetText);

        return rule.HasAnyAction ? rule : null;
    }

    private static List<OptionItem> Build(string leaveLabel, IEnumerable<object> values)
    {
        var list = new List<OptionItem> { new(leaveLabel, null) };
        list.AddRange(values.Select(v => new OptionItem(v.ToString()!, v)));
        return list;
    }

    private static OptionItem? Match(List<OptionItem> options, object? value)
    {
        if (value is null || (value is CpuSetSelection css && css == CpuSetSelection.Unset))
            return options[0];
        return options.FirstOrDefault(o => Equals(o.Value, value)) ?? options[0];
    }

    // Custom CPU sets are entered as core indices ("0-7") and mapped to CPU-set IDs via the topology.
    private List<uint> SetIdsFromIndices(string text)
    {
        if (!AffinityMask.TryParseRangeString(text, 64, out var mask) || mask == 0) return new();
        var indices = AffinityMask.ToIndices(mask).ToHashSet();
        return _topology.Sets.Where(s => indices.Contains(s.LogicalProcessorIndex)).Select(s => s.Id).ToList();
    }

    private string IndicesFromSetIds(List<uint> ids)
    {
        var set = ids.ToHashSet();
        var indices = _topology.Sets.Where(s => set.Contains(s.Id)).Select(s => s.LogicalProcessorIndex);
        return AffinityMask.ToRangeString(AffinityMask.FromIndices(indices));
    }
}
