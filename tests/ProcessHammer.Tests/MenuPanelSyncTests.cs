using System.Diagnostics;
using System.IO;
using ProcessHammer.App.Hardware;
using ProcessHammer.App.ViewModels;
using ProcessHammer.Core.Config;
using ProcessHammer.Core.Logging;
using ProcessHammer.Core.Models;
using ProcessHammer.Core.Services;
using Xunit;

namespace ProcessHammer.Tests;

/// <summary>
/// End-to-end: drives the real MainViewModel menu commands against THIS process and asserts the Rule
/// panel (editor) mirrors the change, and vice-versa. This is the "sync actually works" guarantee.
/// </summary>
public class MenuPanelSyncTests
{
    private static MainViewModel NewVm(out ProcessController controller)
    {
        var config = new AppConfig();
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json"));
        var inspector = new ProcessInspector();
        controller = new ProcessController();
        var topology = new CpuTopology();
        var log = new ActionLog();
        var engine = new RuleEngine(() => config, inspector.Snapshot, controller.ApplyRule, log);
        var monitor = new LiveMonitor();
        var power = new PowerService();
        return new MainViewModel(config, store, inspector, controller, topology, engine, log, monitor, power);
    }

    private static ProcessRowViewModel SelfRow()
    {
        using var self = Process.GetCurrentProcess();
        var snap = new ProcessInspector().Read(self);
        var row = new ProcessRowViewModel(snap);
        row.ExePath = Environment.ProcessPath; // avoid the async exe-path resolve on selection
        return row;
    }

    [Fact]
    public void MenuChange_ReflectsInEditorPanel()
    {
        var vm = NewVm(out var controller);
        var row = SelfRow();
        vm.SelectedProcess = row;

        var original = row.CpuPriorityRaw ?? CpuPriority.Normal;
        var target = original == CpuPriority.BelowNormal ? CpuPriority.Idle : CpuPriority.BelowNormal;
        try
        {
            var menuItem = vm.CpuPriorityMenu.First(m => (CpuPriority?)m.Value == target);
            menuItem.Command.Execute(null); // simulate clicking it in the right-click menu

            Assert.Equal(target, (CpuPriority?)vm.Editor.SelectedCpuPriority?.Value);
        }
        finally { controller.SetCpuPriority(Environment.ProcessId, original); }
    }

    [Fact]
    public void MenuChange_TicksTheMenuCheckmark()
    {
        var vm = NewVm(out var controller);
        var row = SelfRow();
        vm.SelectedProcess = row;

        var original = row.MemoryRaw ?? MemoryPriority.Normal;
        var target = original == MemoryPriority.Low ? MemoryPriority.Medium : MemoryPriority.Low;
        try
        {
            vm.MemoryMenu.First(m => (MemoryPriority?)m.Value == target).Command.Execute(null);

            var checkedItem = vm.MemoryMenu.Single(m => m.IsChecked);
            Assert.Equal(target, (MemoryPriority?)checkedItem.Value);
        }
        finally { controller.SetMemoryPriority(Environment.ProcessId, original); }
    }

    [Fact]
    public void SuccessfulApply_ShowsGreenSuccessToast()
    {
        var vm = NewVm(out var controller);
        var row = SelfRow();
        vm.SelectedProcess = row;

        var original = row.MemoryRaw ?? MemoryPriority.Normal;
        try
        {
            vm.MemoryMenu.First(m => (MemoryPriority?)m.Value == MemoryPriority.Low).Command.Execute(null);

            Assert.True(vm.ToastVisible);
            Assert.Equal("Success!", vm.ToastTitle);
            Assert.False(vm.ToastIsError);
        }
        finally { controller.SetMemoryPriority(Environment.ProcessId, original); }
    }

    [Fact]
    public void PanelChange_AppliesLiveAndTicksMenu()
    {
        var vm = NewVm(out var controller);
        var row = SelfRow();
        vm.SelectedProcess = row;

        var original = row.IoRaw ?? IoPriority.Normal;
        var target = original == IoPriority.Low ? IoPriority.Normal : IoPriority.Low;
        try
        {
            // Change the panel dropdown → should apply live + tick the menu.
            vm.Editor.SelectedIo = vm.Editor.IoOptions.First(o => (IoPriority?)o.Value == target);

            Assert.Equal(target, new ProcessInspector().Read(Process.GetCurrentProcess()).IoPriority);
            Assert.Equal(target, (IoPriority?)vm.IoMenu.Single(m => m.IsChecked).Value);
        }
        finally { controller.SetIoPriority(Environment.ProcessId, original); }
    }
}
