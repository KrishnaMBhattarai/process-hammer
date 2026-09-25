using ProcessHammer.Core.Logging;
using ProcessHammer.Core.Models;

namespace ProcessHammer.Core.Services;

/// <summary>
/// The background watcher: on an interval it snapshots running processes, matches them to the rule
/// set, and (re)applies each matching rule so settings persist even if a process resets them or a
/// new instance launches. Reapplication is idempotent, so running it repeatedly is safe and cheap.
/// </summary>
public sealed class RuleEngine : IDisposable
{
    private readonly Func<IReadOnlyList<ProcessSnapshot>> _snapshot;
    private readonly Func<ProcessRule, int, string?, IReadOnlyList<ActionResult>> _applyRule;
    private readonly Func<int, string?>? _resolveExePath;
    private readonly ActionLog _log;
    private readonly Func<AppConfig> _configProvider;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Remembers which (pid, rule) we've already applied, to avoid log spam every tick.
    private readonly HashSet<(int Pid, string Rule)> _applied = new();

    /// <summary>
    /// Takes the snapshot + apply operations as delegates (rather than the concrete inspector/controller)
    /// so the loop is unit-testable with fakes and stays decoupled from the OS calls.
    /// </summary>
    public RuleEngine(
        Func<AppConfig> configProvider,
        Func<IReadOnlyList<ProcessSnapshot>> snapshot,
        Func<ProcessRule, int, string?, IReadOnlyList<ActionResult>> applyRule,
        ActionLog log,
        Func<int, string?>? resolveExePath = null)
    {
        _configProvider = configProvider;
        _snapshot = snapshot;
        _applyRule = applyRule;
        _log = log;
        _resolveExePath = resolveExePath;
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        _log.Info("Engine started.");
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_loop is not null) await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
        _applied.Clear();
        _log.Info("Engine stopped.");
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var config = _configProvider();
            try { ApplyOnce(config); }
            catch (Exception ex) { _log.Error($"Engine tick failed: {ex.Message}"); }

            var delay = Math.Clamp(config.PollSeconds, 1, 3600);
            try { await Task.Delay(TimeSpan.FromSeconds(delay), token).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>One pass: apply matching rules to all running processes. Public so tests can drive it.</summary>
    public void ApplyOnce(AppConfig config)
    {
        var livePids = new HashSet<int>();

        foreach (var snap in _snapshot())
        {
            livePids.Add(snap.Pid);
            var rule = RuleMatcher.FirstMatch(config.Rules, snap.Name);
            if (rule is null) continue;

            snap.GovernedByRule = rule.Match;
            var key = (snap.Pid, rule.Match);
            var firstTime = _applied.Add(key);

            // GPU preference is a per-exe registry write, so it needs the full image path. The bulk
            // snapshot omits it (too slow for every process), so resolve it here only for the few
            // processes that actually match a rule that sets it.
            var exePath = snap.ExePath;
            if (exePath is null && rule.GpuPreference is not null && _resolveExePath is not null)
                exePath = _resolveExePath(snap.Pid);

            var results = _applyRule(rule, snap.Pid, exePath);
            if (!firstTime) continue; // already logged this pid/rule; keep applying quietly

            foreach (var r in results)
            {
                var msg = $"{snap.Name} (pid {snap.Pid}) [{rule.Match}] {r}";
                if (r.Status is ActionStatus.Failed) _log.Warn(msg);
                else _log.Action(msg);
            }
        }

        // Forget processes that have exited so re-launches get logged afresh.
        _applied.RemoveWhere(k => !livePids.Contains(k.Pid));
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
