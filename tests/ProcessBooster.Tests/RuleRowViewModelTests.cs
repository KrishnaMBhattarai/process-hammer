using ProcessBooster.App.ViewModels;
using ProcessBooster.Core.Models;
using Xunit;

namespace ProcessBooster.Tests;

/// <summary>The Booster Rules tab summarises each saved rule; unset actions must read as "—".</summary>
public class RuleRowViewModelTests
{
    [Fact]
    public void FullRule_SummarisesEveryColumn()
    {
        var row = new RuleRowViewModel(new ProcessRule
        {
            Match = "MyGame",
            Enabled = true,
            CpuPriority = CpuPriority.High,
            AffinityMask = 0b111,
            IoPriority = IoPriority.Low,
            MemoryPriority = MemoryPriority.Normal,
            EfficiencyMode = true,
            DisablePriorityBoost = true,
            CpuSetSelection = CpuSetSelection.PerformanceCores,
            GpuPreference = GpuPreference.HighPerformance,
            GpuSchedulingPriority = GpuSchedulingPriority.High,
            Note = "boost it",
        });

        Assert.Equal("MyGame", row.Process);
        Assert.Equal("Yes", row.EnabledText);
        Assert.Equal("High", row.Cpu);
        Assert.Equal("0-2", row.Affinity);
        Assert.Equal("Low", row.Io);
        Assert.Equal("Normal", row.Memory);
        Assert.Equal("On", row.Eco);
        Assert.Equal("Disabled", row.Boost);
        Assert.Equal("PerformanceCores", row.CpuSets);
        Assert.Equal("HighPerformance", row.GpuPref);
        Assert.Equal("High", row.GpuSched);
        Assert.Equal("boost it", row.Note);
    }

    [Fact]
    public void EmptyRule_ShowsDashesEverywhere()
    {
        var row = new RuleRowViewModel(new ProcessRule { Match = "x", Enabled = false });

        Assert.Equal("No", row.EnabledText);
        Assert.Equal("—", row.Cpu);
        Assert.Equal("—", row.Affinity);
        Assert.Equal("—", row.Io);
        Assert.Equal("—", row.Memory);
        Assert.Equal("—", row.Eco);
        Assert.Equal("—", row.Boost);
        Assert.Equal("—", row.CpuSets);
        Assert.Equal("—", row.GpuPref);
        Assert.Equal("—", row.GpuSched);
        Assert.Equal("", row.Note);
    }

    [Theory]
    [InlineData(true, "On")]
    [InlineData(false, "Off")]
    public void Eco_ReflectsExplicitOnOff(bool mode, string expected) =>
        Assert.Equal(expected, new RuleRowViewModel(new ProcessRule { EfficiencyMode = mode }).Eco);

    [Theory]
    [InlineData(true, "Disabled")]
    [InlineData(false, "Enabled")]
    public void Boost_ReflectsDisableFlag(bool disable, string expected) =>
        Assert.Equal(expected, new RuleRowViewModel(new ProcessRule { DisablePriorityBoost = disable }).Boost);

    [Fact]
    public void AffinityMaskZero_IsTreatedAsAllCores()
    {
        var row = new RuleRowViewModel(new ProcessRule { AffinityMask = 0 });
        Assert.Equal("—", row.Affinity);
    }

    [Fact]
    public void Running_TogglesRunningText()
    {
        var row = new RuleRowViewModel(new ProcessRule { Match = "MyGame" });
        Assert.Equal("Not running", row.RunningText);

        row.Running = true;
        Assert.Equal("Running", row.RunningText);
    }
}
