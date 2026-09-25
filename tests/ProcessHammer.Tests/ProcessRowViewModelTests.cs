using ProcessHammer.App.ViewModels;
using ProcessHammer.Core.Models;
using Xunit;

namespace ProcessHammer.Tests;

public class ProcessRowViewModelTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1L << 10, "1 KB")]
    [InlineData(1L << 20, "1 MB")]
    [InlineData(1L << 30, "1.0 GB")]
    [InlineData(512L, "512 B")]
    public void WorkingSet_FormatsBytes(long bytes, string expected)
    {
        var row = new ProcessRowViewModel(new ProcessSnapshot { Pid = 1, Name = "x", WorkingSetBytes = bytes });
        Assert.Equal(expected, row.WorkingSet);
    }

    [Fact]
    public void Update_MapsAllDisplayAndRawValues()
    {
        var snap = new ProcessSnapshot
        {
            Pid = 42,
            Name = "MyGame",
            AffinityMask = 0b101,
            CpuPriority = CpuPriority.High,
            IoPriority = IoPriority.Low,
            MemoryPriority = MemoryPriority.Normal,
            EfficiencyMode = true,
            PriorityBoostEnabled = false,
            ThreadCount = 12,
            GovernedByRule = "MyGame",
        };
        var row = new ProcessRowViewModel(snap, cpuPercent: 42.5);

        Assert.Equal("MyGame", row.Name);
        Assert.Equal(42.5, row.CpuPercent);
        Assert.Equal("High", row.Cpu);
        Assert.Equal("0,2", row.Affinity);
        Assert.Equal("Low", row.Io);
        Assert.Equal("Normal", row.Memory);
        Assert.Equal("On", row.Eco);
        Assert.Equal(12, row.Threads);
        Assert.Equal("MyGame", row.Rule);

        // Raw values feed the menu/editor "current state".
        Assert.Equal(CpuPriority.High, row.CpuPriorityRaw);
        Assert.Equal(IoPriority.Low, row.IoRaw);
        Assert.Equal(MemoryPriority.Normal, row.MemoryRaw);
        Assert.Equal(true, row.EcoRaw);
        Assert.Equal(false, row.BoostEnabledRaw);
        Assert.Equal(0b101UL, row.AffinityMaskRaw);
    }

    [Fact]
    public void Affinity_AllCoresWhenMaskIsNull()
    {
        var row = new ProcessRowViewModel(new ProcessSnapshot { Pid = 1, Name = "x", AffinityMask = null });
        Assert.Equal("all", row.Affinity);
        Assert.Null(row.AffinityMaskRaw);
    }

    [Fact]
    public void Eco_DashWhenNotOn()
    {
        var row = new ProcessRowViewModel(new ProcessSnapshot { Pid = 1, Name = "x", EfficiencyMode = null });
        Assert.Equal("—", row.Eco);
    }
}
