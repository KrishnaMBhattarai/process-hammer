using ProcessBooster.App.ViewModels;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;
using Xunit;

namespace ProcessBooster.Tests;

/// <summary>
/// The Rule editor must mirror the right-click menu: show the process's CURRENT settings as
/// selected, let a saved rule override them, and round-trip cleanly through BuildRule.
/// </summary>
public class RuleEditorViewModelTests
{
    private static RuleEditorViewModel NewEditor(string target = "MyGame")
    {
        var vm = new RuleEditorViewModel(new CpuTopology());
        vm.SetTarget(target, pid: 1234, exePath: null);
        return vm;
    }

    [Fact]
    public void LoadFrom_NoRule_PreSelectsCurrentState()
    {
        var vm = NewEditor();
        var current = new CurrentState(CpuPriority.High, Affinity: null, IoPriority.Low, MemoryPriority.Normal, Eco: true, BoostEnabled: false);

        vm.LoadFrom(rule: null, current);

        Assert.Equal(CpuPriority.High, vm.SelectedCpuPriority!.Value);
        Assert.Equal(IoPriority.Low, vm.SelectedIo!.Value);
        Assert.Equal(MemoryPriority.Normal, vm.SelectedMemory!.Value);
        Assert.Equal(true, vm.SelectedEfficiency!.Value);
        // BoostEnabled=false → the rule stores "disable boost = true".
        Assert.Equal(true, vm.SelectedBoost!.Value);
    }

    [Fact]
    public void LoadFrom_NoRuleNoCurrent_LeavesEverythingUnchanged()
    {
        var vm = NewEditor();
        vm.LoadFrom(rule: null, current: null);

        Assert.Null(vm.SelectedCpuPriority!.Value);
        Assert.Null(vm.SelectedIo!.Value);
        Assert.Null(vm.SelectedMemory!.Value);
        Assert.Null(vm.SelectedEfficiency!.Value);
        Assert.Null(vm.SelectedBoost!.Value);
    }

    [Fact]
    public void LoadFrom_RuleOverridesCurrent()
    {
        var vm = NewEditor();
        var rule = new ProcessRule { Match = "MyGame", CpuPriority = CpuPriority.Idle };
        var current = new CurrentState(CpuPriority.High, null, IoPriority.Normal, null, null, null);

        vm.LoadFrom(rule, current);

        // Rule wins where it sets a value; current fills the gaps.
        Assert.Equal(CpuPriority.Idle, vm.SelectedCpuPriority!.Value);
        Assert.Equal(IoPriority.Normal, vm.SelectedIo!.Value);
    }

    [Fact]
    public void BuildRule_RoundTripsCurrentStateIntoAppliableRule()
    {
        var vm = NewEditor();
        vm.LoadFrom(null, new CurrentState(CpuPriority.AboveNormal, null, IoPriority.Low, MemoryPriority.Medium, true, true));

        var rule = vm.BuildRule();

        Assert.NotNull(rule);
        Assert.Equal(CpuPriority.AboveNormal, rule!.CpuPriority);
        Assert.Equal(IoPriority.Low, rule.IoPriority);
        Assert.Equal(MemoryPriority.Medium, rule.MemoryPriority);
        Assert.Equal(true, rule.EfficiencyMode);
        Assert.Equal(false, rule.DisablePriorityBoost); // BoostEnabled=true → not disabled
    }

    [Fact]
    public void BuildRule_ReturnsNull_WhenNoTargetSelected()
    {
        var vm = new RuleEditorViewModel(new CpuTopology()); // never SetTarget
        Assert.Null(vm.BuildRule());
    }

    [Fact]
    public void LoadFrom_AffinitySubset_ChecksMatchingCores()
    {
        var vm = NewEditor();
        // Cores 0 and 2 only (0b101).
        var rule = new ProcessRule { Match = "MyGame", AffinityMask = 0b101 };

        vm.LoadFrom(rule);

        Assert.True(vm.AffinityCores[0].IsChecked);
        Assert.False(vm.AffinityCores[1].IsChecked);
        Assert.True(vm.AffinityCores[2].IsChecked);
        Assert.Equal("0,2", vm.AffinityText);

        // And it round-trips back to the same mask.
        Assert.Equal(0b101UL, vm.BuildRule()!.AffinityMask);
    }

    [Fact]
    public void AffinityPreset_All_ChecksEveryCoreAndBlanksText()
    {
        var vm = NewEditor();
        vm.LoadFrom(new ProcessRule { Match = "MyGame", AffinityMask = 0b1 }); // start pinned to core 0

        vm.AffinityPresetCommand.Execute("all");

        Assert.All(vm.AffinityCores, c => Assert.True(c.IsChecked));
        Assert.Equal("", vm.AffinityText);            // all cores = no restriction
        // No affinity restriction remains: the built rule either sets no mask, or (with nothing
        // else selected) is null entirely.
        var rule = vm.BuildRule();
        Assert.True(rule is null || rule.AffinityMask is null);
    }

    [Fact]
    public void UncheckingACore_UpdatesAffinityText()
    {
        var vm = NewEditor();
        vm.AffinityPresetCommand.Execute("all"); // everything ticked
        Assert.Equal("", vm.AffinityText);

        vm.AffinityCores[1].IsChecked = false;   // drop core 1

        Assert.DoesNotContain("1", vm.AffinityText.Split(',', '-'));
        Assert.NotEqual("", vm.AffinityText);
    }
}
