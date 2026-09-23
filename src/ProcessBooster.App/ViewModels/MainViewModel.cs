using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
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

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();
    public ICollectionView ProcessView { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public RuleEditorViewModel Editor { get; }

    public RelayCommand ApplyNowCommand { get; }
    public RelayCommand SaveRuleCommand { get; }
    public RelayCommand RemoveRuleCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public MainViewModel(AppConfig config, ConfigStore store, ProcessInspector inspector,
        ProcessController controller, CpuTopology topology, RuleEngine engine, ActionLog log)
    {
        _config = config; _store = store; _inspector = inspector;
        _controller = controller; _engine = engine; _log = log;

        Editor = new RuleEditorViewModel(topology);

        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.Filter = FilterProcess;
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessRowViewModel.Name), ListSortDirection.Ascending));

        ApplyNowCommand = new RelayCommand(_ => ApplyNow(), _ => SelectedProcess is not null);
        SaveRuleCommand = new RelayCommand(_ => SaveRule(), _ => SelectedProcess is not null);
        RemoveRuleCommand = new RelayCommand(_ => RemoveRule(), _ => SelectedProcess is not null && RuleExistsFor(SelectedProcess.Name));
        ImportCommand = new RelayCommand(_ => ImportConfig());
        ExportCommand = new RelayCommand(_ => ExportConfig());
        RefreshCommand = new RelayCommand(_ => Refresh());

        _log.Logged += OnLogged;
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        foreach (var e in _log.Recent()) LogLines.Add(e.ToString());
        Refresh();
        _timer.Start();
        EngineRunning = true; // auto-run so rules take effect (toggle to pause)
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
                Editor.SetTarget(value.Name, value.Pid, value.ExePath);
                Editor.LoadFrom(FindRule(value.Name));
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
    public void Refresh()
    {
        var snaps = _inspector.Snapshot();
        var existing = Processes.ToDictionary(r => r.Pid);
        var seen = new HashSet<int>();

        foreach (var s in snaps)
        {
            s.GovernedByRule = RuleMatcher.FirstMatch(_config.Rules, s.Name)?.Match;
            seen.Add(s.Pid);
            if (existing.TryGetValue(s.Pid, out var row)) row.Update(s);
            else Processes.Add(new ProcessRowViewModel(s));
        }

        for (var i = Processes.Count - 1; i >= 0; i--)
            if (!seen.Contains(Processes[i].Pid))
                Processes.RemoveAt(i);

        ProcessCount = Processes.Count;
        Raise(nameof(RuleCount));
        Raise(nameof(StatusText));
    }

    private void ApplyNow()
    {
        if (SelectedProcess is not { } p) return;
        var rule = Editor.BuildRule();
        if (rule is null) { _log.Warn($"{p.Name}: nothing to apply (no settings chosen)."); return; }
        var results = _controller.ApplyRule(rule, p.Pid, p.ExePath);
        foreach (var r in results) _log.Action($"{p.Name} (pid {p.Pid}) {r}");
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
