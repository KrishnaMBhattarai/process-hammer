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
    private readonly RuleEngine _engine;
    private readonly ActionLog _log;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<int, TimeSpan> _prevCpu = new();
    private readonly int _cpuCount = Math.Max(1, Environment.ProcessorCount);
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _refreshing;

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();
    public ICollectionView ProcessView { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public RuleEditorViewModel Editor { get; }
    private readonly LiveMonitor _monitor;
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
    public HardwareTabViewModel PowerTab { get; }
    public SensorsTabViewModel SensorsTab { get; }

    public RelayCommand ApplyNowCommand { get; }
    public RelayCommand SaveRuleCommand { get; }
    public RelayCommand RemoveRuleCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand QuickPriorityCommand { get; }
    public RelayCommand QuickEfficiencyCommand { get; }
    public RelayCommand CopySpecsCommand { get; }
    public RelayCommand ExportReportCommand { get; }

    public MainViewModel(AppConfig config, ConfigStore store, ProcessInspector inspector,
        ProcessController controller, CpuTopology topology, RuleEngine engine, ActionLog log, LiveMonitor monitor)
    {
        _config = config; _store = store; _inspector = inspector;
        _controller = controller; _engine = engine; _log = log; _monitor = monitor;

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
        PowerTab = new HardwareTabViewModel(PowerInfoService.Collect);
        SensorsTab = new SensorsTabViewModel(monitor, note);

        // Index 0 = Processes (no hardware VM); the rest map to tabs in the same order as the XAML.
        _tabByIndex = new ITab?[]
        {
            null, SystemTab, OsTab, SecurityTab, UsersTab, CpuTab, MemoryTab, GraphicsTab,
            DisplayTab, StorageTab, NetworkTab, SensorsTab, PowerTab,
        };

        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.Filter = FilterProcess;
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessRowViewModel.Name), ListSortDirection.Ascending));

        ApplyNowCommand = new RelayCommand(_ => ApplyNow(), _ => SelectedProcess is not null);
        SaveRuleCommand = new RelayCommand(_ => SaveRule(), _ => SelectedProcess is not null);
        RemoveRuleCommand = new RelayCommand(_ => RemoveRule(), _ => SelectedProcess is not null && RuleExistsFor(SelectedProcess.Name));
        ImportCommand = new RelayCommand(_ => ImportConfig());
        ExportCommand = new RelayCommand(_ => ExportConfig());
        RefreshCommand = new RelayCommand(_ => Refresh());
        QuickPriorityCommand = new RelayCommand(QuickPriority, _ => SelectedProcess is not null);
        QuickEfficiencyCommand = new RelayCommand(QuickEfficiency, _ => SelectedProcess is not null);
        CopySpecsCommand = new RelayCommand(_ => CopySpecs());
        ExportReportCommand = new RelayCommand(_ => ExportReport());

        _log.Logged += OnLogged;
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        foreach (var e in _log.Recent()) LogLines.Add(e.ToString());
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
            if (value is null) Editor.Clear();
            else
            {
                var pid = value.Pid; var name = value.Name;
                Editor.SetTarget(name, pid, value.ExePath);
                Editor.LoadFrom(FindRule(name));

                // Resolve the exe path lazily off-thread (needed for GPU preference) so selection is instant.
                if (value.ExePath is null)
                {
                    Task.Run(() => _inspector.ReadExePath(pid)).ContinueWith(t =>
                    {
                        if (t.Result is { } path && SelectedProcess?.Pid == pid)
                        {
                            value.ExePath = path;
                            Editor.SetTarget(name, pid, path);
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
            Raise(nameof(RuleCount));
            Raise(nameof(StatusText));
        }
        finally { _refreshing = false; }
    }

    private void ApplyNow()
    {
        if (SelectedProcess is not { } p) return;
        var rule = Editor.BuildRule();
        if (rule is null) { _log.Warn($"{p.Name}: nothing to apply (no settings chosen)."); return; }
        var results = _controller.ApplyRule(rule, p.Pid, p.ExePath);
        foreach (var r in results) _log.Action($"{p.Name} (pid {p.Pid}) {r}");
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
        _log.Action($"{p.Name} (pid {p.Pid}) {_controller.SetEfficiencyMode(p.Pid, on)}");
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
        RaiseCommands();
        Refresh();
    }

    private void RemoveRule()
    {
        if (SelectedProcess is not { } p) return;
        var removed = _config.Rules.RemoveAll(r => r.NormalizedMatch == ProcessRule.Normalize(p.Name));
        if (removed > 0) _log.Info($"Removed rule for {p.Name}.");
        Editor.LoadFrom(null);
        Persist();
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
            _config.RestorePowerPlan = imported.RestorePowerPlan;
            Persist();
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
        ApplyNowCommand.RaiseCanExecuteChanged();
        SaveRuleCommand.RaiseCanExecuteChanged();
        RemoveRuleCommand.RaiseCanExecuteChanged();
        QuickPriorityCommand.RaiseCanExecuteChanged();
        QuickEfficiencyCommand.RaiseCanExecuteChanged();
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
