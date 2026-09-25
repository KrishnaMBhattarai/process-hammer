using ProcessBooster.App.ViewModels;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;
using Xunit;

namespace ProcessBooster.Tests;

/// <summary>
/// The editor's GPU / CPU-set handling, including the custom CPU-set index↔set-id mapping (driven by a
/// known topology injected via the test seam on <see cref="CpuTopology"/>).
/// </summary>
public class RuleEditorCpuSetTests
{
    // Four cores: set-id 10→logical0, 11→1, 12→2, 13→3.
    private static CpuTopology Topo() => new(new[]
    {
        new CpuSetInfo(10, 0, 0, 0), new CpuSetInfo(11, 1, 1, 0),
        new CpuSetInfo(12, 2, 2, 0), new CpuSetInfo(13, 3, 3, 0),
    });

    private static RuleEditorViewModel Editor()
    {
        var vm = new RuleEditorViewModel(Topo());
        vm.SetTarget("MyGame", 1234, null);
        return vm;
    }

    [Fact]
    public void LoadFrom_GpuAndCpuSet_RoundTrip()
    {
        var vm = Editor();
        var rule = new ProcessRule
        {
            Match = "MyGame",
            GpuPreference = GpuPreference.HighPerformance,
            GpuSchedulingPriority = GpuSchedulingPriority.High,
            CpuSetSelection = CpuSetSelection.PerformanceCores,
        };

        vm.LoadFrom(rule);

        Assert.Equal(GpuPreference.HighPerformance, vm.SelectedGpuPreference!.Value);
        Assert.Equal(GpuSchedulingPriority.High, vm.SelectedGpuScheduling!.Value);
        Assert.Equal(CpuSetSelection.PerformanceCores, vm.SelectedCpuSet!.Value);
        Assert.False(vm.IsCustomCpuSet);

        var built = vm.BuildRule()!;
        Assert.Equal(GpuPreference.HighPerformance, built.GpuPreference);
        Assert.Equal(GpuSchedulingPriority.High, built.GpuSchedulingPriority);
        Assert.Equal(CpuSetSelection.PerformanceCores, built.CpuSetSelection);
    }

    [Fact]
    public void LoadFrom_CurrentState_PreSelectsGpuAndCpuSet()
    {
        var vm = Editor();
        var current = new CurrentState(
            Cpu: null, Affinity: null, Io: null, Memory: null, Eco: null, BoostEnabled: null,
            CpuSets: CpuSetSelection.PerformanceCores,
            GpuScheduling: GpuSchedulingPriority.AboveNormal,
            GpuPreference: GpuPreference.PowerSaving);

        vm.LoadFrom(rule: null, current);

        Assert.Equal(CpuSetSelection.PerformanceCores, vm.SelectedCpuSet!.Value);
        Assert.Equal(GpuSchedulingPriority.AboveNormal, vm.SelectedGpuScheduling!.Value);
        Assert.Equal(GpuPreference.PowerSaving, vm.SelectedGpuPreference!.Value);
    }

    [Fact]
    public void Custom_IndicesMapToSetIds_AndBack()
    {
        var vm = Editor();
        var rule = new ProcessRule { Match = "MyGame", CpuSetSelection = CpuSetSelection.Custom, CpuSetIds = new() { 10, 12 } };

        vm.LoadFrom(rule);

        Assert.True(vm.IsCustomCpuSet);
        Assert.Equal("0,2", vm.CustomCpuSetText); // set-ids 10,12 → logical cores 0,2

        var built = vm.BuildRule()!;
        Assert.Equal(CpuSetSelection.Custom, built.CpuSetSelection);
        Assert.Equal(new List<uint> { 10, 12 }, built.CpuSetIds); // and back to the same set-ids
    }

    [Fact]
    public void Custom_WithTypedIndices_ProducesMatchingSetIds()
    {
        var vm = Editor();
        vm.SelectedCpuSet = vm.CpuSetOptions.First(o => (CpuSetSelection?)o.Value == CpuSetSelection.Custom);
        vm.CustomCpuSetText = "1-3";

        var built = vm.BuildRule()!;
        Assert.Equal(new List<uint> { 11, 12, 13 }, built.CpuSetIds);
    }

    [Fact]
    public void FullCurrentState_ReflectsInEveryFieldAndRoundTrips()
    {
        var vm = Editor();
        var current = new CurrentState(
            Cpu: CpuPriority.High, Affinity: 0b101, Io: IoPriority.Low, Memory: MemoryPriority.Medium,
            Eco: true, BoostEnabled: false,
            CpuSets: CpuSetSelection.PerformanceCores, GpuScheduling: GpuSchedulingPriority.High,
            GpuPreference: GpuPreference.HighPerformance);

        vm.LoadFrom(rule: null, current);

        // Every field mirrors the current state (this is what the menu and panel share).
        Assert.Equal(CpuPriority.High, vm.SelectedCpuPriority!.Value);
        Assert.Equal(IoPriority.Low, vm.SelectedIo!.Value);
        Assert.Equal(MemoryPriority.Medium, vm.SelectedMemory!.Value);
        Assert.Equal(true, vm.SelectedEfficiency!.Value);
        Assert.Equal(true, vm.SelectedBoost!.Value); // boost enabled=false → disable-boost=true
        Assert.Equal(CpuSetSelection.PerformanceCores, vm.SelectedCpuSet!.Value);
        Assert.Equal(GpuSchedulingPriority.High, vm.SelectedGpuScheduling!.Value);
        Assert.Equal(GpuPreference.HighPerformance, vm.SelectedGpuPreference!.Value);
        Assert.Equal("0,2", vm.AffinityText);

        // ...and BuildRule reproduces all of it (so Apply persists exactly what's shown).
        var r = vm.BuildRule()!;
        Assert.Equal(CpuPriority.High, r.CpuPriority);
        Assert.Equal(0b101UL, r.AffinityMask);
        Assert.Equal(IoPriority.Low, r.IoPriority);
        Assert.Equal(MemoryPriority.Medium, r.MemoryPriority);
        Assert.Equal(true, r.EfficiencyMode);
        Assert.Equal(true, r.DisablePriorityBoost);
        Assert.Equal(CpuSetSelection.PerformanceCores, r.CpuSetSelection);
        Assert.Equal(GpuSchedulingPriority.High, r.GpuSchedulingPriority);
        Assert.Equal(GpuPreference.HighPerformance, r.GpuPreference);
    }

    [Fact]
    public void RuleOverridesCurrent_PerField()
    {
        var vm = Editor();
        var rule = new ProcessRule { Match = "MyGame", CpuPriority = CpuPriority.Idle, GpuPreference = GpuPreference.PowerSaving };
        var current = new CurrentState(
            Cpu: CpuPriority.High, Affinity: null, Io: IoPriority.Normal, Memory: null, Eco: null, BoostEnabled: null,
            CpuSets: CpuSetSelection.All, GpuScheduling: GpuSchedulingPriority.Normal, GpuPreference: GpuPreference.HighPerformance);

        vm.LoadFrom(rule, current);

        Assert.Equal(CpuPriority.Idle, vm.SelectedCpuPriority!.Value);              // rule wins
        Assert.Equal(GpuPreference.PowerSaving, vm.SelectedGpuPreference!.Value);   // rule wins
        Assert.Equal(IoPriority.Normal, vm.SelectedIo!.Value);                      // current fills the gap
        Assert.Equal(GpuSchedulingPriority.Normal, vm.SelectedGpuScheduling!.Value);// current fills the gap
        Assert.Equal(CpuSetSelection.All, vm.SelectedCpuSet!.Value);                // current fills the gap
    }

    [Fact]
    public void IsCustomCpuSet_ReflectsSelection()
    {
        var vm = Editor();
        vm.SelectedCpuSet = vm.CpuSetOptions.First(o => (CpuSetSelection?)o.Value == CpuSetSelection.PerformanceCores);
        Assert.False(vm.IsCustomCpuSet);
        vm.SelectedCpuSet = vm.CpuSetOptions.First(o => (CpuSetSelection?)o.Value == CpuSetSelection.Custom);
        Assert.True(vm.IsCustomCpuSet);
    }
}
