using System.Diagnostics;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;
using ProcessBooster.Core.Util;
using Xunit;

namespace ProcessBooster.Tests;

/// <summary>
/// Functional tests that apply each boosting action to a REAL process and read the value back, proving
/// the whole path (P/Invoke + read-back) actually works. Windows-only, like the product. State is
/// always restored in a finally so the test host is left untouched.
/// </summary>
public class ProcessControllerIntegrationTests
{
    private readonly ProcessController _controller = new();
    private readonly ProcessInspector _inspector = new();
    private int Pid => Environment.ProcessId;

    private ProcessSnapshot Read()
    {
        using var self = Process.GetCurrentProcess();
        return _inspector.Read(self);
    }

    [Fact]
    public void Affinity_SetSubsetAndReadBack()
    {
        if (Environment.ProcessorCount < 2) return; // needs at least two cores to pin a subset
        var original = Read().AffinityMask is { } m && m != 0 ? m : AffinityMask.All(Environment.ProcessorCount);
        try
        {
            var r = _controller.SetAffinity(Pid, 0b11); // cores 0 and 1
            Assert.Equal(ActionStatus.Applied, r.Status);
            Assert.Equal(0b11UL, Read().AffinityMask);
        }
        finally { _controller.SetAffinity(Pid, original); }
    }

    [Fact]
    public void Affinity_ZeroMaskIsSkipped()
    {
        var r = _controller.SetAffinity(Pid, 0);
        Assert.Equal(ActionStatus.Skipped, r.Status);
    }

    [Fact]
    public void CpuSets_SetPerformanceThenClear_ReadBack()
    {
        try
        {
            Assert.Equal(ActionStatus.Applied, _controller.SetCpuSets(Pid, CpuSetSelection.PerformanceCores, null).Status);
            Assert.Equal(CpuSetSelection.PerformanceCores, _controller.ReadCpuSets(Pid));

            Assert.Equal(ActionStatus.Applied, _controller.SetCpuSets(Pid, CpuSetSelection.All, null).Status);
            Assert.Equal(CpuSetSelection.All, _controller.ReadCpuSets(Pid)); // cleared = all cores
        }
        finally { _controller.SetCpuSets(Pid, CpuSetSelection.All, null); }
    }

    [Fact]
    public void CpuSets_UnsetIsSkipped() =>
        Assert.Equal(ActionStatus.Skipped, _controller.SetCpuSets(Pid, CpuSetSelection.Unset, null).Status);

    [Fact]
    public void GpuPreference_RegistryRoundTrip()
    {
        // A throwaway exe path keeps the test isolated from any real app's preference.
        const string fakeExe = @"C:\__processbooster_test__\game.exe";
        try
        {
            Assert.Equal(ActionStatus.Applied, _controller.SetGpuPreference(fakeExe, GpuPreference.HighPerformance).Status);
            Assert.Equal(GpuPreference.HighPerformance, _controller.ReadGpuPreference(fakeExe));
        }
        finally
        {
            _controller.SetGpuPreference(fakeExe, GpuPreference.SystemDefault); // removes the value
            Assert.Equal(GpuPreference.SystemDefault, _controller.ReadGpuPreference(fakeExe));
        }
    }

    [Fact]
    public void GpuScheduling_SetIsAppliedAndRoundTripsWhenReadable()
    {
        // Setting the GPU scheduling class is driver-dependent; when it applies AND the read-back is
        // supported, they must agree. We never assert a hard failure on hardware that lacks it.
        var set = _controller.SetGpuSchedulingPriority(Pid, GpuSchedulingPriority.High);
        Assert.Contains(set.Status, new[] { ActionStatus.Applied, ActionStatus.Failed });
        if (set.Status == ActionStatus.Applied)
        {
            var read = _controller.ReadGpuScheduling(Pid);
            if (read is not null) Assert.Equal(GpuSchedulingPriority.High, read);
            _controller.SetGpuSchedulingPriority(Pid, GpuSchedulingPriority.Normal);
        }
    }

    [Fact]
    public void TrimWorkingSet_Applies() =>
        Assert.Equal(ActionStatus.Applied, _controller.TrimWorkingSet(Pid).Status);

    [Fact]
    public void ReadCurrentExtras_ReturnsReadableState()
    {
        var extras = _controller.ReadCurrentExtras(Pid, exePath: null);
        Assert.NotNull(extras.CpuSets); // at minimum "All" is readable for our own process
    }

    [Fact]
    public void Terminate_KillsASpawnedProcess()
    {
        var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 30")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            Assert.Equal(ActionStatus.Applied, _controller.Terminate(child.Id).Status);
            Assert.True(child.WaitForExit(5000));
            Assert.True(child.HasExited);
        }
        finally { if (!child.HasExited) child.Kill(); child.Dispose(); }
    }

    [Fact]
    public void Restart_WithUnknownPath_FailsCleanly()
    {
        var r = _controller.Restart(Pid, exePath: null, asAdmin: false);
        Assert.Equal(ActionStatus.Failed, r.Status);
    }

    [Fact]
    public void Close_OnProcessWithoutMainWindow_IsSkipped()
    {
        // The test host is a console process with no main window, so a graceful close is a no-op.
        var r = _controller.Close(Pid);
        Assert.Equal(ActionStatus.Skipped, r.Status);
    }
}

/// <summary>Power-plan enumeration + switching against the real host (no lasting change).</summary>
public class PowerServiceIntegrationTests
{
    private readonly PowerService _power = new();

    [Fact]
    public void ListSchemes_ReturnsAtLeastOnePlan()
    {
        var schemes = _power.ListSchemes();
        Assert.NotEmpty(schemes);
        Assert.All(schemes, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
    }

    [Fact]
    public void GetActiveScheme_IsAmongTheListedSchemes()
    {
        var active = _power.GetActiveScheme();
        Assert.NotNull(active);
        Assert.Contains(_power.ListSchemes(), s => s.Guid == active!.Value);
    }

    [Fact]
    public void SetActiveScheme_ToCurrent_Succeeds()
    {
        var active = _power.GetActiveScheme();
        Assert.NotNull(active);
        Assert.True(_power.SetActiveScheme(active!.Value)); // set to what it already is → no real change
    }
}
