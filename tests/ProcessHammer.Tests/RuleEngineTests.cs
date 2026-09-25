using System.Threading.Tasks;
using ProcessHammer.Core.Logging;
using ProcessHammer.Core.Models;
using ProcessHammer.Core.Services;
using Xunit;

namespace ProcessHammer.Tests;

/// <summary>
/// The auto-apply loop, driven with fake snapshot/apply delegates (no OS). Verifies matching,
/// idempotent re-apply, once-only logging, and that exited processes are forgotten.
/// </summary>
public class RuleEngineTests
{
    private static ProcessSnapshot Snap(int pid, string name) => new() { Pid = pid, Name = name };

    private static (RuleEngine engine, List<(int pid, string rule)> applied, ActionLog log) Build(
        AppConfig config, Func<IReadOnlyList<ProcessSnapshot>> snapshot)
    {
        var applied = new List<(int, string)>();
        var log = new ActionLog();
        IReadOnlyList<ActionResult> Apply(ProcessRule rule, int pid, string? exe)
        {
            applied.Add((pid, rule.Match));
            return new[] { new ActionResult("CpuPriority", ActionStatus.Applied) };
        }
        return (new RuleEngine(() => config, snapshot, Apply, log), applied, log);
    }

    private static AppConfig ConfigWith(params ProcessRule[] rules)
    {
        var c = new AppConfig();
        c.Rules.AddRange(rules);
        return c;
    }

    [Fact]
    public void ApplyOnce_AppliesOnlyToMatchingProcesses()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", CpuPriority = CpuPriority.High });
        var (engine, applied, _) = Build(config, () => new[] { Snap(1, "MyGame"), Snap(2, "chrome") });

        engine.ApplyOnce(config);

        Assert.Single(applied);
        Assert.Equal((1, "mygame"), applied[0]);
    }

    [Fact]
    public void ApplyOnce_SkipsDisabledRules()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", Enabled = false, CpuPriority = CpuPriority.High });
        var (engine, applied, _) = Build(config, () => new[] { Snap(1, "mygame") });

        engine.ApplyOnce(config);

        Assert.Empty(applied);
    }

    [Fact]
    public void ApplyOnce_ReAppliesEveryTick_ButLogsOnlyOnce()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", CpuPriority = CpuPriority.High });
        var (engine, applied, log) = Build(config, () => new[] { Snap(1, "mygame") });

        engine.ApplyOnce(config);
        engine.ApplyOnce(config);

        Assert.Equal(2, applied.Count);                          // re-applied both ticks (idempotent enforcement)
        Assert.Single(log.Recent(), e => e.Level == LogLevel.Action); // logged only the first time
    }

    [Fact]
    public void ApplyOnce_RelaunchAfterExit_LogsAgain()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", CpuPriority = CpuPriority.High });
        var live = new List<ProcessSnapshot> { Snap(1, "mygame") };
        var (engine, applied, log) = Build(config, () => live.ToArray());

        engine.ApplyOnce(config);        // pid 1 seen + logged
        live.Clear();                    // process exits
        engine.ApplyOnce(config);        // nothing live → pid 1 forgotten
        live.Add(Snap(1, "mygame"));  // relaunch (same pid)
        engine.ApplyOnce(config);        // logs afresh

        Assert.Equal(2, log.Recent().Count(e => e.Level == LogLevel.Action));
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public async Task StartThenStop_RunsTheLoopAndReportsRunning()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", CpuPriority = CpuPriority.High });
        config.PollSeconds = 1;
        var (engine, applied, _) = Build(config, () => new[] { Snap(1, "mygame") });

        engine.Start();                 // runs ApplyOnce immediately, then polls
        Assert.True(engine.IsRunning);
        await Task.Delay(200);
        await engine.StopAsync();

        Assert.False(engine.IsRunning);
        Assert.True(applied.Count >= 1);
    }

    [Fact]
    public void ApplyOnce_ResolvesExePath_ForGpuPreferenceRules()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", GpuPreference = GpuPreference.HighPerformance });
        string? received = "SENTINEL";
        IReadOnlyList<ActionResult> Apply(ProcessRule r, int pid, string? exe) { received = exe; return new[] { new ActionResult("GpuPreference", ActionStatus.Applied) }; }
        var engine = new RuleEngine(() => config, () => new[] { Snap(1, "mygame") }, Apply, new ActionLog(),
            resolveExePath: pid => $"C:\\games\\{pid}.exe");

        engine.ApplyOnce(config);

        Assert.Equal("C:\\games\\1.exe", received); // engine resolved the path the snapshot lacked
    }

    [Fact]
    public void ApplyOnce_SkipsExePathResolution_WhenRuleHasNoGpuPreference()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", CpuPriority = CpuPriority.High });
        var resolverCalled = false;
        IReadOnlyList<ActionResult> Apply(ProcessRule r, int pid, string? exe) => new[] { new ActionResult("CpuPriority", ActionStatus.Applied) };
        var engine = new RuleEngine(() => config, () => new[] { Snap(1, "mygame") }, Apply, new ActionLog(),
            resolveExePath: _ => { resolverCalled = true; return "x"; });

        engine.ApplyOnce(config);

        Assert.False(resolverCalled); // no GPU-pref action → no (potentially slow) path lookup
    }

    [Fact]
    public void ApplyOnce_LogsFailuresAsWarnings()
    {
        var config = ConfigWith(new ProcessRule { Match = "mygame", CpuPriority = CpuPriority.High });
        var log = new ActionLog();
        IReadOnlyList<ActionResult> Apply(ProcessRule r, int pid, string? e) =>
            new[] { new ActionResult("CpuPriority", ActionStatus.Failed, "denied") };
        var engine = new RuleEngine(() => config, () => new[] { Snap(1, "mygame") }, Apply, log);

        engine.ApplyOnce(config);

        Assert.Contains(log.Recent(), e => e.Level == LogLevel.Warn);
    }
}
