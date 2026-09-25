using System.Diagnostics;
using ProcessHammer.Core.Models;
using ProcessHammer.Core.Services;
using Xunit;

namespace ProcessHammer.Tests;

/// <summary>Pure mapping tests (no OS state touched).</summary>
public class PriorityMapTests
{
    [Theory]
    [InlineData(CpuPriority.Idle)]
    [InlineData(CpuPriority.BelowNormal)]
    [InlineData(CpuPriority.Normal)]
    [InlineData(CpuPriority.AboveNormal)]
    [InlineData(CpuPriority.High)]
    [InlineData(CpuPriority.Realtime)]
    public void PriorityClass_RoundTrips(CpuPriority p) =>
        Assert.Equal(p, ProcessController.FromNative(ProcessController.ToNative(p)));
}

/// <summary>
/// Real integration tests against THIS process. They mutate then restore process state, proving the
/// native calls succeed and read-back agrees. Windows-only (the whole product is).
/// </summary>
public class SelfProcessIntegrationTests
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
    public void CpuPriority_SetAndReadBack()
    {
        var original = Read().CpuPriority ?? CpuPriority.Normal;
        try
        {
            var r = _controller.SetCpuPriority(Pid, CpuPriority.BelowNormal);
            Assert.Equal(ActionStatus.Applied, r.Status);
            Assert.Equal(CpuPriority.BelowNormal, Read().CpuPriority);
        }
        finally { _controller.SetCpuPriority(Pid, original); }
    }

    [Fact]
    public void MemoryPriority_SetAndReadBack()
    {
        try
        {
            var r = _controller.SetMemoryPriority(Pid, MemoryPriority.Low);
            Assert.Equal(ActionStatus.Applied, r.Status);
            Assert.Equal(MemoryPriority.Low, Read().MemoryPriority);
        }
        finally { _controller.SetMemoryPriority(Pid, MemoryPriority.Normal); }
    }

    [Fact]
    public void EfficiencyMode_EnableThenDisable()
    {
        try
        {
            Assert.Equal(ActionStatus.Applied, _controller.SetEfficiencyMode(Pid, true).Status);
            Assert.True(Read().EfficiencyMode);

            // Disabling resets to system-managed, which Windows reports as "not explicitly set" (null),
            // so the contract we verify is simply: it is no longer reported as ON.
            Assert.Equal(ActionStatus.Applied, _controller.SetEfficiencyMode(Pid, false).Status);
            Assert.NotEqual(true, Read().EfficiencyMode);
        }
        finally { _controller.SetEfficiencyMode(Pid, false); }
    }

    [Fact]
    public void IoPriority_SetLow()
    {
        var r = _controller.SetIoPriority(Pid, IoPriority.Low);
        Assert.Equal(ActionStatus.Applied, r.Status);
        Assert.Equal(IoPriority.Low, Read().IoPriority);
        _controller.SetIoPriority(Pid, IoPriority.Normal);
    }

    [Fact]
    public void PriorityBoost_ToggleAndReadBack()
    {
        var original = Read().PriorityBoostEnabled ?? true;
        try
        {
            Assert.Equal(ActionStatus.Applied, _controller.SetPriorityBoostDisabled(Pid, true).Status);
            Assert.False(Read().PriorityBoostEnabled);
        }
        finally { _controller.SetPriorityBoostDisabled(Pid, !original); }
    }
}

/// <summary>CPU topology should enumerate real sets on the host running the tests.</summary>
public class CpuTopologyTests
{
    [Fact]
    public void EnumeratesCpuSets()
    {
        var topo = new CpuTopology();
        Assert.NotEmpty(topo.Sets);
        Assert.NotEmpty(topo.PerformanceCoreSetIds());
    }
}
