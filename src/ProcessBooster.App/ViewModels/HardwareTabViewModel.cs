using System.Collections.ObjectModel;
using ProcessBooster.App.Hardware;

namespace ProcessBooster.App.ViewModels;

/// <summary>
/// Generic backing VM for every hardware info tab: static <see cref="Sections"/> collected once
/// (off the UI thread) plus optional live <see cref="Tiles"/> fed from the shared <see cref="LiveMonitor"/>.
/// </summary>
public sealed class HardwareTabViewModel : ViewModelBase, IDisposable
{
    private readonly Func<List<InfoSection>> _collect;
    private readonly LiveMonitor? _monitor;
    private readonly (string Label, Func<LiveMonitor, string> Read)[] _tileDefs;

    public ObservableCollection<InfoSection> Sections { get; } = new();
    public ObservableCollection<StatTile> Tiles { get; } = new();
    public string? Note { get; }

    public HardwareTabViewModel(
        Func<List<InfoSection>> collect,
        LiveMonitor? monitor = null,
        (string Label, Func<LiveMonitor, string> Read)[]? tiles = null,
        string? note = null)
    {
        _collect = collect;
        _monitor = monitor;
        _tileDefs = tiles ?? Array.Empty<(string, Func<LiveMonitor, string>)>();
        Note = note;

        foreach (var (label, _) in _tileDefs) Tiles.Add(new StatTile(label));
    }

    private bool _loaded;
    private bool _live;

    /// <summary>Called when the user opens this tab: load static specs once, then start live tiles.</summary>
    public async void Activate()
    {
        if (!_loaded)
        {
            _loaded = true;
            var sections = await Task.Run(_collect);   // static specs: pulled ONCE, off the UI thread
            foreach (var s in sections) Sections.Add(s);
        }
        if (_monitor is not null && _tileDefs.Length > 0 && !_live)
        {
            _live = true;
            _monitor.Updated += OnUpdated;
            _monitor.AddConsumer();   // sensor polling runs only while a live tab is active
            OnUpdated();
        }
    }

    /// <summary>Called when the user leaves this tab: stop consuming live readings.</summary>
    public void Deactivate()
    {
        if (!_live) return;
        _live = false;
        _monitor!.Updated -= OnUpdated;
        _monitor.RemoveConsumer();
    }

    private void OnUpdated()
    {
        if (_monitor is null) return;
        for (var i = 0; i < Tiles.Count && i < _tileDefs.Length; i++)
            Tiles[i].Value = _tileDefs[i].Read(_monitor);
    }

    public void Dispose() => Deactivate();
}

/// <summary>Formatting helpers for live tile readers.</summary>
public static class Live
{
    public static string Fmt(double? v, string suffix) => v is { } d ? $"{d:0}{suffix}" : "—";
    public static string Ghz(double? mhz) => mhz is { } v ? $"{v / 1000.0:0.00} GHz" : "—";
    public static string Mhz(double? mhz) => mhz is { } v ? $"{v:0} MHz" : "—";
    public static string Gb(double? gb) => gb is { } v ? $"{v:0.0} GB" : "—";
    public static string Mbps(double v) => v >= 1000 ? $"{v / 1000.0:0.0} Gbps" : $"{v:0.0} Mbps";
    public static string Vram(double? used, double? total) =>
        used is { } u ? (total is { } t ? $"{u:0.0}/{t:0.0} GB" : $"{u:0.0} GB") : "—";
}
