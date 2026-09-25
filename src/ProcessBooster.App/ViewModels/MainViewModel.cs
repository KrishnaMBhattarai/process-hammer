using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using ProcessBooster.App.Hardware;
using ProcessBooster.Core.Config;
using ProcessBooster.Core.Logging;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;

namespace ProcessBooster.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly ProcessInspector _inspector;
    private readonly ProcessController _controller;
    private readonly PowerService _power;
    private readonly CpuTopology _topology;
    private readonly RuleEngine _engine;
    private readonly ActionLog _log;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<int, TimeSpan> _prevCpu = new();
    private readonly int _cpuCount = Math.Max(1, Environment.ProcessorCount);
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _refreshing;
    private bool _suppressAffinity;

    /// <summary>Booster Rules is the second tab (index 1), right after Processes.</summary>
    private const int BoosterTabIndex = 1;

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();
    public ICollectionView ProcessView { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public RuleEditorViewModel Editor { get; }

    // Right-click menu backing collections.
    public ObservableCollection<AffinityCoreItem> AffinityCores { get; } = new();
    public ObservableCollection<PowerProfileItem> PowerProfiles { get; } = new();

    // Checkable submenus that reflect the selected process's current settings.
    public ObservableCollection<MenuOptionItem> CpuPriorityMenu { get; } = new();
    public ObservableCollection<MenuOptionItem> IoMenu { get; } = new();
    public ObservableCollection<MenuOptionItem> MemoryMenu { get; } = new();
    public ObservableCollection<MenuOptionItem> EfficiencyMenu { get; } = new();
    public ObservableCollection<MenuOptionItem> BoostMenu { get; } = new();
    public ObservableCollection<MenuOptionItem> CpuSetMenu { get; } = new();
    public ObservableCollection<MenuOptionItem> GpuSchedulingMenu { get; } = new();
    public ObservableCollection<MenuOptionItem> GpuPreferenceMenu { get; } = new();

    // Current per-process state that isn't in the live table (read once when a row is selected).
    private ProcessController.CurrentExtras _extras;

    // Affinity-preset checkmarks (which of All / P-cores / E-cores matches the current mask).
    public bool AffinityIsAllCores => CurrentAffinityPresetKey() == "all";
    public bool AffinityIsPerformance => CurrentAffinityPresetKey() == "p";
    public bool AffinityIsEfficiency => CurrentAffinityPresetKey() == "e";

    // Booster Rules tab.
    public ObservableCollection<RuleRowViewModel> BoosterRules { get; } = new();
    public bool HasNoBoosterRules => BoosterRules.Count == 0;
    private RuleRowViewModel? _selectedRule;
    public RuleRowViewModel? SelectedRule
    {
        get => _selectedRule;
        set { if (SetField(ref _selectedRule, value)) RaiseRuleCommands(); }
    }
    private readonly ITab?[] _tabByIndex;

    public HardwareTabViewModel SystemTab { get; }
    public HardwareTabViewModel OsTab { get; }
    public HardwareTabViewModel CpuTab { get; }
    public HardwareTabViewModel MemoryTab { get; }
    public HardwareTabViewModel GraphicsTab { get; }
    public HardwareTabViewModel DisplayTab { get; }
    public HardwareTabViewModel StorageTab { get; }
    public HardwareTabViewModel NetworkTab { get; }
    public HardwareTabViewModel SecurityTab { get; }
    public HardwareTabViewModel UsersTab { get; }
    public TableTabViewModel StartupTab { get; }
    public TableTabViewModel SoftwareTab { get; }
    public TableTabViewModel ServicesTab { get; }
    public HardwareTabViewModel DevicesTab { get; }
    public TableTabViewModel EnvironmentTab { get; }
    public HardwareTabViewModel PowerTab { get; }
    public SensorsTabViewModel SensorsTab { get; }

    public RelayCommand SaveRuleCommand { get; }
    public RelayCommand RemoveRuleCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CopySpecsCommand { get; }
    public RelayCommand ExportReportCommand { get; }

    // Right-click boosting actions. (CPU priority / I/O / memory / efficiency / boost / CPU sets /
    // GPU priority / GPU preference use the checkable menu collections, so no commands here.)
    public RelayCommand AffinityPresetCommand { get; }
    public RelayCommand TrimMemoryCommand { get; }
    public RelayCommand CopyRuleCommand { get; }
    public RelayCommand RestartProcessCommand { get; }
    public RelayCommand RestartAsAdminCommand { get; }
    public RelayCommand CloseProcessCommand { get; }
    public RelayCommand TerminateProcessCommand { get; }

    // Booster Rules tab actions.
    public RelayCommand ApplySelectedRuleCommand { get; }
    public RelayCommand ToggleRuleEnabledCommand { get; }
    public RelayCommand RemoveSelectedRuleCommand { get; }
    public RelayCommand RemoveAllRulesCommand { get; }

    public MainViewModel(AppConfig config, ConfigStore store, ProcessInspector inspector,
        ProcessController controller, CpuTopology topology, RuleEngine engine, ActionLog log, LiveMonitor monitor,
        PowerService power)
    {
        _config = config; _store = store; _inspector = inspector;
        _controller = controller; _engine = engine; _log = log; _power = power;
        _topology = topology;

        Editor = new RuleEditorViewModel(topology);

        var note = monitor.SensorNote is { } n
            ? $"Live sensors couldn't start ({n}). Static specs are still shown; live temps/clocks/fan need the sensor driver + admin."
            : null;

        (string, Func<LiveMonitor, string>)[] Cpu() => new[]
        {
            ("CPU temp", (Func<LiveMonitor, string>)(m => Live.Fmt(m.Sensors.CpuTempC, "°C"))),
            ("CPU load", m => Live.Fmt(m.Sensors.CpuLoad, "%")),
            ("CPU clock", m => Live.Ghz(m.Sensors.CpuClockMhz)),
            ("CPU power", m => Live.Fmt(m.Sensors.CpuPowerW, " W")),
        };

        SystemTab = new HardwareTabViewModel(SystemInfoService.Collect, monitor, new[]
        {
            ("CPU temp", (Func<LiveMonitor, string>)(m => Live.Fmt(m.Sensors.CpuTempC, "°C"))),
            ("CPU load", m => Live.Fmt(m.Sensors.CpuLoad, "%")),
            ("Memory", m => Live.Gb(m.Sensors.MemUsedGb)),
            ("GPU temp", m => Live.Fmt(m.Sensors.GpuTempC, "°C")),
            ("GPU load", m => Live.Fmt(m.Sensors.GpuLoad, "%")),
            ("Net down", m => Live.Mbps(m.NetDownMbps)),
            ("Net up", m => Live.Mbps(m.NetUpMbps)),
        }, note);

        CpuTab = new HardwareTabViewModel(
            () => { var l = CpuInfoService.Collect(); l.AddRange(CpuCoreSections(topology)); return l; },
            monitor, Cpu(), note,
            new[]
            {
                new SparklineViewModel("CPU load", m => m.Sensors.CpuLoad, "%"),
                new SparklineViewModel("CPU temperature", m => m.Sensors.CpuTempC, "°C"),
            });

        MemoryTab = new HardwareTabViewModel(MemoryInfoService.Collect, monitor, new[]
        {
            ("Used", (Func<LiveMonitor, string>)(m => Live.Gb(m.Sensors.MemUsedGb))),
            ("Load", m => Live.Fmt(m.Sensors.MemLoad, "%")),
        }, note,
        new[] { new SparklineViewModel("Memory usage", m => m.Sensors.MemLoad, "%") });

        GraphicsTab = new HardwareTabViewModel(GraphicsInfoService.Collect, monitor, new[]
        {
            ("GPU temp", (Func<LiveMonitor, string>)(m => Live.Fmt(m.Sensors.GpuTempC, "°C"))),
            ("GPU load", m => Live.Fmt(m.Sensors.GpuLoad, "%")),
            ("GPU clock", m => Live.Mhz(m.Sensors.GpuClockMhz)),
            ("GPU memory", m => Live.Vram(m.Sensors.GpuVramUsedGb, m.Sensors.GpuVramTotalGb)),
        }, note,
        new[]
        {
            new SparklineViewModel("GPU load", m => m.Sensors.GpuLoad, "%"),
            new SparklineViewModel("GPU temperature", m => m.Sensors.GpuTempC, "°C"),
        });

        DisplayTab = new HardwareTabViewModel(DisplayInfoService.Collect);
        StorageTab = new HardwareTabViewModel(StorageInfoService.Collect);

        NetworkTab = new HardwareTabViewModel(NetworkInfoService.Collect, monitor, new[]
        {
            ("Download", (Func<LiveMonitor, string>)(m => Live.Mbps(m.NetDownMbps))),
            ("Upload", m => Live.Mbps(m.NetUpMbps)),
        }, note,
        new[]
        {
            new SparklineViewModel("Download", m => m.NetDownMbps, " Mbps"),
            new SparklineViewModel("Upload", m => m.NetUpMbps, " Mbps"),
        });

        OsTab = new HardwareTabViewModel(OsInfoService.Collect);
        SecurityTab = new HardwareTabViewModel(SecurityInfoService.Collect);
        UsersTab = new HardwareTabViewModel(UsersInfoService.Collect);
        StartupTab = new TableTabViewModel(StartupInfoService.CollectTables);
        SoftwareTab = new TableTabViewModel(SoftwareInfoService.CollectTables);
        ServicesTab = new TableTabViewModel(ServicesInfoService.CollectTables);
        DevicesTab = new HardwareTabViewModel(DevicesInfoService.Collect);
        EnvironmentTab = new TableTabViewModel(EnvironmentInfoService.CollectTables);
        PowerTab = new HardwareTabViewModel(PowerInfoService.Collect);
        SensorsTab = new SensorsTabViewModel(monitor, note);

        // Index 0 = Processes, index 1 = Booster Rules (neither has a hardware VM); the rest map to
        // tabs in the same order as the XAML.
        _tabByIndex = new ITab?[]
        {
            null, null, SystemTab, OsTab, SecurityTab, UsersTab, StartupTab, SoftwareTab, ServicesTab,
            DevicesTab, EnvironmentTab, CpuTab, MemoryTab, GraphicsTab,
            DisplayTab, StorageTab, NetworkTab, SensorsTab, PowerTab,
        };

        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.Filter = FilterProcess;
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessRowViewModel.Name), ListSortDirection.Ascending));

        SaveRuleCommand = new RelayCommand(_ => SaveRule(), _ => SelectedProcess is not null);
        RemoveRuleCommand = new RelayCommand(_ => RemoveRule(), _ => SelectedProcess is not null && RuleExistsFor(SelectedProcess.Name));
        ImportCommand = new RelayCommand(_ => ImportConfig());
        ExportCommand = new RelayCommand(_ => ExportConfig());
        RefreshCommand = new RelayCommand(_ => Refresh());
        CopySpecsCommand = new RelayCommand(_ => CopySpecs());
        ExportReportCommand = new RelayCommand(_ => ExportReport());

        AffinityPresetCommand = new RelayCommand(ApplyAffinityPreset);
        TrimMemoryCommand = new RelayCommand(_ => TrimMemory());
        CopyRuleCommand = new RelayCommand(_ => CopyRule());
        RestartProcessCommand = new RelayCommand(_ => RestartProcess(asAdmin: false));
        RestartAsAdminCommand = new RelayCommand(_ => RestartProcess(asAdmin: true));
        CloseProcessCommand = new RelayCommand(_ => CloseProcess());
        TerminateProcessCommand = new RelayCommand(_ => TerminateProcess());

        ApplySelectedRuleCommand = new RelayCommand(_ => ApplySelectedRule(), _ => SelectedRule is not null);
        ToggleRuleEnabledCommand = new RelayCommand(_ => ToggleRuleEnabled(), _ => SelectedRule is not null);
        RemoveSelectedRuleCommand = new RelayCommand(_ => RemoveSelectedRule(), _ => SelectedRule is not null);
        RemoveAllRulesCommand = new RelayCommand(_ => RemoveAllRules(), _ => _config.Rules.Count > 0);

        // Checkable submenus (a tick marks the value currently applied to the selected process).
        foreach (CpuPriority v in Enum.GetValues<CpuPriority>()) CpuPriorityMenu.Add(MenuItem(Humanize(v.ToString()), v, QuickPriority));
        foreach (IoPriority v in Enum.GetValues<IoPriority>()) IoMenu.Add(MenuItem(Humanize(v.ToString()), v, QuickIo));
        foreach (MemoryPriority v in Enum.GetValues<MemoryPriority>()) MemoryMenu.Add(MenuItem(Humanize(v.ToString()), v, QuickMemory));
        EfficiencyMenu.Add(MenuItem("On (EcoQoS)", true, QuickEfficiency));
        EfficiencyMenu.Add(MenuItem("Off (system-managed)", false, QuickEfficiency));
        BoostMenu.Add(MenuItem("Enabled", true, QuickBoostEnabled));   // Value = "boost enabled?" for the checkmark
        BoostMenu.Add(MenuItem("Disabled", false, QuickBoostEnabled));
        CpuSetMenu.Add(MenuItem("All cores", CpuSetSelection.All, QuickCpuSet));
        CpuSetMenu.Add(MenuItem("Performance cores", CpuSetSelection.PerformanceCores, QuickCpuSet));
        CpuSetMenu.Add(MenuItem("Efficiency cores", CpuSetSelection.EfficiencyCores, QuickCpuSet));
        foreach (GpuSchedulingPriority v in Enum.GetValues<GpuSchedulingPriority>()) GpuSchedulingMenu.Add(MenuItem(Humanize(v.ToString()), v, QuickGpuScheduling));
        foreach (GpuPreference v in Enum.GetValues<GpuPreference>()) GpuPreferenceMenu.Add(MenuItem(Humanize(v.ToString()), v, QuickGpuPreference));

        // One checkbox per logical CPU for the right-click affinity list.
        for (var i = 0; i < _cpuCount; i++)
        {
            var index = i;
            AffinityCores.Add(new AffinityCoreItem(index, ApplyAffinityFromCores));
        }

        // Populate the power-plan list once (they rarely change); each item switches the active plan.
        try
        {
            foreach (var s in _power.ListSchemes())
            {
                var item = new PowerProfileItem(s.Guid, s.Name);
                item.SelectCommand = new RelayCommand(_ => SetPowerProfile(item));
                PowerProfiles.Add(item);
            }
            RefreshActivePowerProfile();
        }
        catch (Exception ex) { _log.Warn("Could not read power plans: " + ex.Message); }

        _log.Logged += OnLogged;
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        foreach (var e in _log.Recent()) LogLines.Add(e.ToString());
        RebuildBoosterRules();
        Refresh();
        _timer.Start();       // Processes tab (index 0) is active on launch
        EngineRunning = true; // auto-run so rules take effect (toggle to pause)
    }

    /// <summary>
    /// Bound to the tab strip. Switching tabs pauses the work of the tab you left and starts only the
    /// tab you opened: the Processes list refreshes only while visible; a hardware tab loads its static
    /// specs once (first visit) and polls live sensors only while it is the active tab.
    /// </summary>
    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { var old = _selectedTabIndex; if (SetField(ref _selectedTabIndex, value)) OnTabChanged(old, value); }
    }

    private void OnTabChanged(int oldIndex, int newIndex)
    {
        if (oldIndex == 0) _timer.Stop();
        else if (oldIndex > 0 && oldIndex < _tabByIndex.Length) _tabByIndex[oldIndex]?.Deactivate();

        if (newIndex == 0) { _timer.Start(); Refresh(); }
        else if (newIndex == BoosterTabIndex) RebuildBoosterRules(); // refresh saved rules + running status on open
        else if (newIndex > 0 && newIndex < _tabByIndex.Length) _tabByIndex[newIndex]?.Activate();
    }

    /// <summary>
    /// Per-logical-processor listing. On a hybrid CPU, Performance and Efficiency cores are returned as
    /// two separate sections (rendered as two side-by-side columns); otherwise one section. Fully generic —
    /// the P/E split comes from the OS-reported EfficiencyClass, nothing hardcoded to a particular chip.
    /// </summary>
    private static IEnumerable<InfoSection> CpuCoreSections(CpuTopology topo)
    {
        var sets = topo.Sets.OrderBy(x => x.LogicalProcessorIndex).ToList();
        if (sets.Count == 0)
            return new[] { new InfoSection { Title = "Logical processors", Items = { new InfoItem("Cores", "—") } } };

        var maxEff = sets.Max(x => x.EfficiencyClass);
        if (maxEff == sets.Min(x => x.EfficiencyClass))
        {
            var one = new InfoSection { Title = "Logical processors" };
            foreach (var c in sets) one.Items.Add(new InfoItem($"CPU {c.LogicalProcessorIndex}", $"Core {c.CoreIndex}"));
            return new[] { one };
        }

        var perf = new InfoSection { Title = "Performance cores" };
        var eff = new InfoSection { Title = "Efficiency cores" };
        foreach (var c in sets)
            (c.EfficiencyClass == maxEff ? perf : eff).Items.Add(
                new InfoItem($"CPU {c.LogicalProcessorIndex}", $"Core {c.CoreIndex}"));
        return new[] { perf, eff };
    }

    // ---- selection ----
    private ProcessRowViewModel? _selected;
    public ProcessRowViewModel? SelectedProcess
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            // Read the current settings that aren't in the live table (CPU sets, GPU priority/preference)
            // once per selection, so both the menu checkmarks and the editor reflect reality.
            _extras = value is null ? default : _controller.ReadCurrentExtras(value.Pid, value.ExePath);
            RefreshAffinityCores(value?.AffinityMaskRaw);
            RefreshMenuChecks();
            if (value is null) Editor.Clear();
            else
            {
                var pid = value.Pid; var name = value.Name;
                Editor.SetTarget(name, pid, value.ExePath);
                Editor.LoadFrom(FindRule(name), CurrentOf(value));

                // Resolve the exe path lazily off-thread (needed for GPU preference) so selection is instant.
                if (value.ExePath is null)
                {
                    Task.Run(() => _inspector.ReadExePath(pid)).ContinueWith(t =>
                    {
                        if (t.Result is { } path && SelectedProcess?.Pid == pid)
                        {
                            value.ExePath = path;
                            _extras = _controller.ReadCurrentExtras(pid, path); // now GPU preference is readable
                            Editor.SetTarget(name, pid, path);
                            Editor.LoadFrom(FindRule(name), CurrentOf(value));
                            RefreshMenuChecks();
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
            }
            RaiseCommands();
        }
    }

    // ---- search ----
    private string _search = "";
    public string SearchText
    {
        get => _search;
        set { if (SetField(ref _search, value)) ProcessView.Refresh(); }
    }

    // ---- engine ----
    private bool _engineRunning;
    public bool EngineRunning
    {
        get => _engineRunning;
        set
        {
            if (!SetField(ref _engineRunning, value)) return;
            if (value) _engine.Start();
            else _ = _engine.StopAsync();
            Raise(nameof(StatusText));
        }
    }

    // ---- status ----
    private bool _isAdmin;
    public bool IsAdmin { get => _isAdmin; private set { if (SetField(ref _isAdmin, value)) Raise(nameof(StatusText)); } }

    private int _processCount;
    public int ProcessCount { get => _processCount; private set { if (SetField(ref _processCount, value)) Raise(nameof(StatusText)); } }

    public int RuleCount => _config.Rules.Count;

    public string StatusText =>
        $"{ProcessCount} processes  ·  {RuleCount} rule(s)  ·  engine {(EngineRunning ? "running" : "paused")}  ·  {(IsAdmin ? "admin" : "NOT admin")}";

    // ---- core operations ----
    public async void Refresh()
    {
        if (_refreshing) return;      // never overlap scans
        _refreshing = true;
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = Math.Max(0.001, (now - _lastTick).TotalSeconds);
            _lastTick = now;

            // Heavy work (process enumeration + per-process native reads) OFF the UI thread,
            // so scrolling, selection and the whole window stay responsive.
            var snaps = await Task.Run(() => _inspector.Snapshot()).ConfigureAwait(true);

            var existing = Processes.ToDictionary(r => r.Pid);
            var seen = new HashSet<int>();

            foreach (var s in snaps)
            {
                s.GovernedByRule = RuleMatcher.FirstMatch(_config.Rules, s.Name)?.Match;
                seen.Add(s.Pid);

                double cpu = 0;
                if (_prevCpu.TryGetValue(s.Pid, out var prev))
                    cpu = Math.Clamp((s.CpuTime - prev).TotalSeconds / elapsed / _cpuCount * 100.0, 0, 100);
                _prevCpu[s.Pid] = s.CpuTime;

                if (existing.TryGetValue(s.Pid, out var row)) row.Update(s, cpu);
                else Processes.Add(new ProcessRowViewModel(s, cpu));
            }

            for (var i = Processes.Count - 1; i >= 0; i--)
                if (!seen.Contains(Processes[i].Pid))
                    Processes.RemoveAt(i);

            foreach (var pid in _prevCpu.Keys.Where(k => !seen.Contains(k)).ToList())
                _prevCpu.Remove(pid);

            ProcessCount = Processes.Count;
            UpdateRulesRunning();
            Raise(nameof(RuleCount));
            Raise(nameof(StatusText));
        }
        finally { _refreshing = false; }
    }

    // Right-click quick actions — apply immediately to the selected process (one-shot, no rule saved).
    private void QuickPriority(object? param)
    {
        if (SelectedProcess is not { } p || param is not CpuPriority prio) return;
        _log.Action($"{p.Name} (pid {p.Pid}) {_controller.SetCpuPriority(p.Pid, prio)}");
    }

    private void QuickEfficiency(object? param)
    {
        if (SelectedProcess is not { } p) return;
        var on = param is bool b ? b : string.Equals(param?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        Act(p, _controller.SetEfficiencyMode(p.Pid, on));
    }

    private void Act(ProcessRowViewModel p, ActionResult r) => _log.Action($"{p.Name} (pid {p.Pid}) {r}");

    /// <summary>Resolve the exe path on demand (needed for GPU preference + restart) if not already known.</summary>
    private void EnsureExePath(ProcessRowViewModel p)
    {
        if (p.ExePath is not null) return;
        try { p.ExePath = _inspector.ReadExePath(p.Pid); } catch { /* best effort */ }
    }

    private void QuickIo(object? param)
    {
        if (SelectedProcess is { } p && param is IoPriority io) Act(p, _controller.SetIoPriority(p.Pid, io));
    }

    private void QuickMemory(object? param)
    {
        if (SelectedProcess is { } p && param is MemoryPriority mem) Act(p, _controller.SetMemoryPriority(p.Pid, mem));
    }

    private void QuickGpuScheduling(object? param)
    {
        if (SelectedProcess is { } p && param is GpuSchedulingPriority g) Act(p, _controller.SetGpuSchedulingPriority(p.Pid, g));
    }

    private void QuickGpuPreference(object? param)
    {
        if (SelectedProcess is not { } p || param is not GpuPreference g) return;
        EnsureExePath(p);
        Act(p, _controller.SetGpuPreference(p.ExePath, g));
    }

    private void QuickCpuSet(object? param)
    {
        if (SelectedProcess is { } p && param is CpuSetSelection sel) Act(p, _controller.SetCpuSets(p.Pid, sel, null));
    }

    private void QuickBoostEnabled(object? param)
    {
        if (SelectedProcess is not { } p) return;
        var enabled = param is bool b && b;
        Act(p, _controller.SetPriorityBoostDisabled(p.Pid, disabled: !enabled));
    }

    /// <summary>Wrap an apply action so the checkmarks re-read fresh state right after it runs.</summary>
    private MenuOptionItem MenuItem(string label, object? value, Action<object?> apply) =>
        new(label, value, o => { apply(o); RefreshSelectedState(); });

    /// <summary>Re-read the selected process's live settings from the OS, then refresh all checkmarks.</summary>
    private void RefreshSelectedState()
    {
        if (SelectedProcess is { } p)
        {
            _extras = _controller.ReadCurrentExtras(p.Pid, p.ExePath);
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById(p.Pid);
                p.Update(_inspector.Read(proc), p.CpuPercent);
            }
            catch { /* process may have exited */ }
            RefreshAffinityCores(p.AffinityMaskRaw);
            // Keep the Rule panel mirrored to the menu: reload it from the new current state.
            Editor.LoadFrom(FindRule(p.Name), CurrentOf(p));
        }
        RefreshMenuChecks();
    }

    /// <summary>Tick the menu item matching each current setting on the selected process.</summary>
    private void RefreshMenuChecks()
    {
        var p = SelectedProcess;
        SetChecks(CpuPriorityMenu, p?.CpuPriorityRaw);
        SetChecks(IoMenu, p?.IoRaw);
        SetChecks(MemoryMenu, p?.MemoryRaw);
        SetChecks(EfficiencyMenu, p?.EcoRaw);
        SetChecks(BoostMenu, p?.BoostEnabledRaw);
        SetChecks(CpuSetMenu, IsPresetCpuSet(_extras.CpuSets) ? _extras.CpuSets : null);
        SetChecks(GpuSchedulingMenu, _extras.GpuScheduling);
        SetChecks(GpuPreferenceMenu, _extras.GpuPreference);
        Raise(nameof(AffinityIsAllCores));
        Raise(nameof(AffinityIsPerformance));
        Raise(nameof(AffinityIsEfficiency));
    }

    private static bool IsPresetCpuSet(CpuSetSelection? s) =>
        s is CpuSetSelection.All or CpuSetSelection.PerformanceCores or CpuSetSelection.EfficiencyCores;

    /// <summary>
    /// Which affinity preset ("all"/"p"/"e") matches the current core selection, if any. Reads the
    /// checkbox state (the live source of truth for affinity in the menu), so it stays correct as the
    /// user ticks cores or picks a preset.
    /// </summary>
    private string? CurrentAffinityPresetKey()
    {
        if (SelectedProcess is null) return null;
        ulong mask = 0;
        foreach (var c in AffinityCores) if (c.IsChecked) mask |= 1UL << c.Index;
        if (mask == 0) mask = AllCoresMask();
        if (mask == AllCoresMask()) return "all";
        if (_topology.IsHybrid && mask == CoreClassMask(performance: true)) return "p";
        if (_topology.IsHybrid && mask == CoreClassMask(performance: false)) return "e";
        return null;
    }

    private static void SetChecks(IEnumerable<MenuOptionItem> items, object? current)
    {
        foreach (var it in items) it.IsChecked = current is not null && Equals(it.Value, current);
    }

    /// <summary>"BelowNormal" → "Below normal", "VeryLow" → "Very low".</summary>
    private static string Humanize(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var ch = pascal[i];
            if (i > 0 && char.IsUpper(ch)) { sb.Append(' '); sb.Append(char.ToLower(ch)); }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    // ---- CPU affinity checkbox list ----
    private ulong AllCoresMask() => AffinityPresets.AllMask(_cpuCount);

    /// <summary>Rebuild the affinity checkboxes from a process's current mask, without triggering an apply.</summary>
    private void RefreshAffinityCores(ulong? mask)
    {
        _suppressAffinity = true;
        try
        {
            var effective = mask is { } m && m != 0 ? m : AllCoresMask();
            foreach (var c in AffinityCores) c.IsChecked = (effective & (1UL << c.Index)) != 0;
        }
        finally { _suppressAffinity = false; }
    }

    private void ApplyAffinityFromCores()
    {
        if (_suppressAffinity || SelectedProcess is not { } p) return;
        ulong mask = 0;
        foreach (var c in AffinityCores) if (c.IsChecked) mask |= 1UL << c.Index;
        if (mask == 0) mask = AllCoresMask(); // unchecking everything = all cores
        Act(p, _controller.SetAffinity(p.Pid, mask));
        RefreshSelectedState(); // mirror to preset checkmarks + the Rule panel
    }

    private ulong CoreClassMask(bool performance) => AffinityPresets.ClassMask(_topology.Sets, performance, _cpuCount);

    /// <summary>Right-click affinity presets: "all" (P+E), "p" (performance cores), "e" (efficiency cores).</summary>
    private void ApplyAffinityPreset(object? param)
    {
        if (SelectedProcess is not { } p) return;
        var which = param?.ToString();
        var mask = which switch
        {
            "p" => CoreClassMask(performance: true),
            "e" => CoreClassMask(performance: false),
            _ => AllCoresMask(),
        };
        if (which == "e" && !_topology.IsHybrid) { _log.Warn($"{p.Name}: no separate efficiency cores on this CPU."); return; }
        Act(p, _controller.SetAffinity(p.Pid, mask));
        RefreshAffinityCores(mask); // reflect the preset in the checkboxes
        Raise(nameof(AffinityIsAllCores));
        Raise(nameof(AffinityIsPerformance));
        Raise(nameof(AffinityIsEfficiency));
    }

    // ---- lifecycle actions ----
    private void TrimMemory() { if (SelectedProcess is { } p) Act(p, _controller.TrimWorkingSet(p.Pid)); }

    private void TerminateProcess()
    {
        if (SelectedProcess is not { } p) return;
        Act(p, _controller.Terminate(p.Pid));
        Refresh();
    }

    private void CloseProcess() { if (SelectedProcess is { } p) Act(p, _controller.Close(p.Pid)); }

    private void RestartProcess(bool asAdmin)
    {
        if (SelectedProcess is not { } p) return;
        EnsureExePath(p);
        Act(p, _controller.Restart(p.Pid, p.ExePath, asAdmin));
    }

    private void CopyRule()
    {
        if (SelectedProcess is not { } p) return;
        var rule = FindRule(p.Name);
        var text = rule is not null
            ? ConfigStore.Serialize(new AppConfig { Rules = { rule } })
            : $"{p.Name}: no saved rule. Current — priority {p.Cpu}, cores {p.Affinity}, I/O {p.Io}, memory {p.Memory}, eco {p.Eco}";
        try { System.Windows.Clipboard.SetText(text); _log.Info($"Copied rule/settings for {p.Name} to clipboard."); }
        catch (Exception ex) { _log.Error("Copy failed: " + ex.Message); }
    }

    // ---- power profile ----
    private void SetPowerProfile(PowerProfileItem item)
    {
        if (_power.SetActiveScheme(item.Guid)) { _log.Info($"Power plan → {item.Name}."); RefreshActivePowerProfile(); }
        else _log.Warn($"Could not switch to power plan {item.Name}.");
    }

    private void RefreshActivePowerProfile()
    {
        var active = _power.GetActiveScheme();
        foreach (var it in PowerProfiles) it.IsActive = active is { } a && a == it.Guid;
    }

    // ---- Booster Rules tab ----
    private void RebuildBoosterRules()
    {
        BoosterRules.Clear();
        foreach (var r in _config.Rules.OrderBy(r => r.Match, StringComparer.OrdinalIgnoreCase))
            BoosterRules.Add(new RuleRowViewModel(r));
        UpdateRulesRunning();
        RemoveAllRulesCommand.RaiseCanExecuteChanged();
        Raise(nameof(HasNoBoosterRules));
        Raise(nameof(RuleCount));
        Raise(nameof(StatusText));
    }

    private void UpdateRulesRunning()
    {
        if (BoosterRules.Count == 0) return;
        var running = Processes.Select(p => ProcessRule.Normalize(p.Name)).ToHashSet();
        foreach (var rr in BoosterRules) rr.Running = running.Contains(rr.Rule.NormalizedMatch);
    }

    private void ApplySelectedRule()
    {
        if (SelectedRule is not { } rr) return;
        var any = false;
        foreach (var p in Processes.Where(p => ProcessRule.Normalize(p.Name) == rr.Rule.NormalizedMatch))
        {
            any = true;
            foreach (var res in _controller.ApplyRule(rr.Rule, p.Pid, p.ExePath)) Act(p, res);
        }
        if (!any) _log.Info($"No running process matches rule '{rr.Process}'.");
    }

    private void ToggleRuleEnabled()
    {
        if (SelectedRule is not { } rr) return;
        rr.Rule.Enabled = !rr.Rule.Enabled;
        _log.Info($"{rr.Process} rule {(rr.Rule.Enabled ? "enabled" : "disabled")}.");
        Persist();
        RebuildBoosterRules();
    }

    private void RemoveSelectedRule()
    {
        if (SelectedRule is not { } rr) return;
        _config.Rules.RemoveAll(r => r.NormalizedMatch == rr.Rule.NormalizedMatch);
        _log.Info($"Removed rule for {rr.Process}.");
        Persist();
        RebuildBoosterRules();
        RaiseCommands();
    }

    private void RemoveAllRules()
    {
        if (_config.Rules.Count == 0) return;
        var n = _config.Rules.Count;
        _config.Rules.Clear();
        _log.Info($"Removed all {n} rule(s).");
        Persist();
        RebuildBoosterRules();
        RaiseCommands();
    }

    private void RaiseRuleCommands()
    {
        ApplySelectedRuleCommand.RaiseCanExecuteChanged();
        ToggleRuleEnabledCommand.RaiseCanExecuteChanged();
        RemoveSelectedRuleCommand.RaiseCanExecuteChanged();
    }

    private void SaveRule()
    {
        if (SelectedProcess is not { } p) return;
        var rule = Editor.BuildRule();
        _config.Rules.RemoveAll(r => r.NormalizedMatch == ProcessRule.Normalize(p.Name));
        if (rule is not null)
        {
            _config.Rules.Add(rule);
            _log.Info($"Saved rule for {rule.Match}.");
            foreach (var r in _controller.ApplyRule(rule, p.Pid, p.ExePath)) _log.Action($"{p.Name} (pid {p.Pid}) {r}");
        }
        else _log.Info($"Cleared rule for {p.Name} (no settings chosen).");
        Persist();
        RebuildBoosterRules();
        RaiseCommands();
        RefreshSelectedState(); // mirror the just-applied settings back into the menu checkmarks
        Refresh();
    }

    private void RemoveRule()
    {
        if (SelectedProcess is not { } p) return;
        var removed = _config.Rules.RemoveAll(r => r.NormalizedMatch == ProcessRule.Normalize(p.Name));
        if (removed > 0) _log.Info($"Removed rule for {p.Name}.");
        Editor.LoadFrom(null, CurrentOf(p)); // keep showing the process's live state
        Persist();
        RebuildBoosterRules();
        RaiseCommands();
        Refresh();
    }

    private void ImportConfig()
    {
        var dlg = new OpenFileDialog { Filter = "Process Booster config (*.json)|*.json|All files (*.*)|*.*", Title = "Import config" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var imported = ConfigStore.Import(dlg.FileName);
            _config.Rules.Clear();
            _config.Rules.AddRange(imported.Rules);
            _config.PollSeconds = imported.PollSeconds;
            Persist();
            RebuildBoosterRules();
            _log.Info($"Imported {imported.Rules.Count} rule(s) from {Path.GetFileName(dlg.FileName)}.");
            Refresh();
        }
        catch (Exception ex) { _log.Error($"Import failed: {ex.Message}"); }
    }

    private void ExportConfig()
    {
        var dlg = new SaveFileDialog { Filter = "Process Booster config (*.json)|*.json", Title = "Export config", FileName = "process-booster.json" };
        if (dlg.ShowDialog() != true) return;
        try { ConfigStore.Export(_config, dlg.FileName); _log.Info($"Exported config to {Path.GetFileName(dlg.FileName)}."); }
        catch (Exception ex) { _log.Error($"Export failed: {ex.Message}"); }
    }

    private async void CopySpecs()
    {
        try
        {
            var text = await Task.Run(Hardware.ReportBuilder.Build);
            System.Windows.Clipboard.SetText(text);
            _log.Info("Copied full system report to clipboard.");
        }
        catch (Exception ex) { _log.Error("Copy specs failed: " + ex.Message); }
    }

    private async void ExportReport()
    {
        var dlg = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", Title = "Export system report", FileName = "process-booster-report.txt" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var text = await Task.Run(Hardware.ReportBuilder.Build);
            File.WriteAllText(dlg.FileName, text);
            _log.Info("Exported system report to " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { _log.Error("Export report failed: " + ex.Message); }
    }

    // ---- helpers ----
    private bool FilterProcess(object o)
    {
        if (string.IsNullOrWhiteSpace(_search)) return true;
        if (o is not ProcessRowViewModel r) return false;
        return r.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || r.Pid.ToString().Contains(_search);
    }

    /// <summary>Snapshot a process row's live settings (+ the just-read extras) for the editor pre-select.</summary>
    private CurrentState CurrentOf(ProcessRowViewModel p) =>
        new(p.CpuPriorityRaw, p.AffinityMaskRaw, p.IoRaw, p.MemoryRaw, p.EcoRaw, p.BoostEnabledRaw,
            _extras.CpuSets, _extras.GpuScheduling, _extras.GpuPreference);

    private ProcessRule? FindRule(string name) =>
        _config.Rules.FirstOrDefault(r => r.NormalizedMatch == ProcessRule.Normalize(name));

    private bool RuleExistsFor(string name) => FindRule(name) is not null;

    private void Persist()
    {
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error($"Could not save config: {ex.Message}"); }
    }

    private void RaiseCommands()
    {
        SaveRuleCommand.RaiseCanExecuteChanged();
        RemoveRuleCommand.RaiseCanExecuteChanged();
    }

    private void OnLogged(LogEntry entry)
    {
        var line = entry.ToString();
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LogLines.Insert(0, line);
            while (LogLines.Count > 500) LogLines.RemoveAt(LogLines.Count - 1);
        });
    }
}
