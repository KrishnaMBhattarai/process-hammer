using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using ProcessBooster.App.Hardware;

namespace ProcessBooster.App.ViewModels;

/// <summary>A tab that activates/deactivates as the user switches to/from it (lazy load + live polling).</summary>
public interface ITab
{
    void Activate();
    void Deactivate();
}

/// <summary>A tiny live history graph: keeps a rolling window and exposes a polyline + current value.</summary>
public sealed class SparklineViewModel : ViewModelBase
{
    private readonly Func<LiveMonitor, double?> _read;
    private readonly string _suffix;
    private readonly int _capacity;
    private readonly Queue<double> _history = new();

    public string Title { get; }
    public double GraphWidth { get; } = 300;
    public double GraphHeight { get; } = 46;

    private PointCollection _points = new();
    public PointCollection Points { get => _points; private set => SetField(ref _points, value); }

    private string _current = "—";
    public string Current { get => _current; private set => SetField(ref _current, value); }

    public SparklineViewModel(string title, Func<LiveMonitor, double?> read, string suffix, int capacity = 60)
    {
        Title = title; _read = read; _suffix = suffix; _capacity = capacity;
    }

    public void Push(LiveMonitor m)
    {
        var v = _read(m);
        if (v is null) return;
        _history.Enqueue(v.Value);
        while (_history.Count > _capacity) _history.Dequeue();
        Current = $"{v.Value:0.#}{_suffix}";

        var data = _history.ToArray();
        var pts = new PointCollection();
        if (data.Length >= 2)
        {
            var min = data.Min();
            var max = data.Max();
            var range = max - min;
            if (range < 1e-6) range = 1;
            for (var i = 0; i < data.Length; i++)
            {
                var x = (double)i / (data.Length - 1) * GraphWidth;
                var norm = (data[i] - min) / range;
                var y = GraphHeight - 3 - norm * (GraphHeight - 6);
                pts.Add(new Point(x, y));
            }
        }
        Points = pts;
    }
}

public sealed class SensorReadingViewModel : ViewModelBase
{
    public string Name { get; }
    private string _value;
    public string Value { get => _value; set => SetField(ref _value, value); }
    public SensorReadingViewModel(string name, string value) { Name = name; _value = value; }
}

public sealed class SensorGroupViewModel
{
    public string Name { get; }
    public ObservableCollection<SensorReadingViewModel> Readings { get; } = new();
    public SensorGroupViewModel(string name) => Name = name;
}

/// <summary>The Sensors tab: a live, in-place-updating dump of every hardware sensor, grouped by device.</summary>
public sealed class SensorsTabViewModel : ViewModelBase, ITab
{
    private readonly LiveMonitor _monitor;
    private bool _live;

    public ObservableCollection<SensorGroupViewModel> Groups { get; } = new();
    public string? Note { get; }

    public SensorsTabViewModel(LiveMonitor monitor, string? note)
    {
        _monitor = monitor;
        Note = note;
    }

    public void Activate()
    {
        if (_live) return;
        _live = true;
        _monitor.Updated += OnUpdated;
        _monitor.AddConsumer();
        OnUpdated();
    }

    public void Deactivate()
    {
        if (!_live) return;
        _live = false;
        _monitor.Updated -= OnUpdated;
        _monitor.RemoveConsumer();
    }

    private void OnUpdated()
    {
        foreach (var src in _monitor.SensorGroups)
        {
            var g = Groups.FirstOrDefault(x => x.Name == src.Hardware);
            if (g is null) { g = new SensorGroupViewModel(src.Hardware); Groups.Add(g); }
            foreach (var line in src.Lines)
            {
                var r = g.Readings.FirstOrDefault(x => x.Name == line.Name);
                if (r is null) g.Readings.Add(new SensorReadingViewModel(line.Name, line.Value));
                else r.Value = line.Value;
            }
        }
    }
}

/// <summary>A tab that renders one or more <see cref="DataTable"/>s as sortable data grids (loaded once).</summary>
public sealed class TableTabViewModel : ViewModelBase, ITab
{
    private readonly Func<List<DataTable>> _collect;
    private bool _loaded;

    public ObservableCollection<DataTable> Tables { get; } = new();

    public TableTabViewModel(Func<List<DataTable>> collect) => _collect = collect;

    public async void Activate()
    {
        if (_loaded) return;
        _loaded = true;
        var tables = await Task.Run(_collect);   // WMI/registry off the UI thread
        foreach (var t in tables) Tables.Add(t);
    }

    public void Deactivate() { }
}
