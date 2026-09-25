using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ProcessHammer.Core.Interop;
using ProcessHammer.Core.Models;

namespace ProcessHammer.Core.Services;

/// <summary>
/// Applies process settings via the native APIs. Each method is independent and returns an
/// <see cref="ActionResult"/> rather than throwing, so a partial failure never aborts a whole rule.
/// </summary>
public sealed class ProcessController
{
    private readonly CpuTopology _topology;
    private readonly GpuPreferenceStore _gpuPrefs;

    public ProcessController(CpuTopology? topology = null, GpuPreferenceStore? gpuPrefs = null)
    {
        _topology = topology ?? new CpuTopology();
        _gpuPrefs = gpuPrefs ?? new GpuPreferenceStore();
    }

    // ---- priority-class mapping (kept internal + testable via ToNative/FromNative) ----
    public static uint ToNative(CpuPriority p) => p switch
    {
        CpuPriority.Idle => NativeMethods.IDLE_PRIORITY_CLASS,
        CpuPriority.BelowNormal => NativeMethods.BELOW_NORMAL_PRIORITY_CLASS,
        CpuPriority.Normal => NativeMethods.NORMAL_PRIORITY_CLASS,
        CpuPriority.AboveNormal => NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS,
        CpuPriority.High => NativeMethods.HIGH_PRIORITY_CLASS,
        CpuPriority.Realtime => NativeMethods.REALTIME_PRIORITY_CLASS,
        _ => NativeMethods.NORMAL_PRIORITY_CLASS,
    };

    public static CpuPriority? FromNative(uint priorityClass) => priorityClass switch
    {
        NativeMethods.IDLE_PRIORITY_CLASS => CpuPriority.Idle,
        NativeMethods.BELOW_NORMAL_PRIORITY_CLASS => CpuPriority.BelowNormal,
        NativeMethods.NORMAL_PRIORITY_CLASS => CpuPriority.Normal,
        NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS => CpuPriority.AboveNormal,
        NativeMethods.HIGH_PRIORITY_CLASS => CpuPriority.High,
        NativeMethods.REALTIME_PRIORITY_CLASS => CpuPriority.Realtime,
        _ => null,
    };

    private static SafeProcessHandle Open(int pid, uint access) =>
        NativeMethods.OpenProcess(access, false, pid);

    private static ActionResult Fail(string action) =>
        new(action, ActionStatus.Failed, $"win32 error {Marshal.GetLastWin32Error()}");

    public ActionResult SetCpuPriority(int pid, CpuPriority priority)
    {
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("CpuPriority");
        return NativeMethods.SetPriorityClass(h, ToNative(priority))
            ? new("CpuPriority", ActionStatus.Applied, priority.ToString())
            : Fail("CpuPriority");
    }

    public ActionResult SetAffinity(int pid, ulong mask)
    {
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("Affinity");
        if (mask == 0) return new("Affinity", ActionStatus.Skipped, "mask 0 = all cores, left unchanged");
        return NativeMethods.SetProcessAffinityMask(h, (UIntPtr)mask)
            ? new("Affinity", ActionStatus.Applied, Util.AffinityMask.ToRangeString(mask))
            : Fail("Affinity");
    }

    public ActionResult SetIoPriority(int pid, IoPriority io)
    {
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("IoPriority");
        var value = (int)io;
        var status = NativeMethods.NtSetInformationProcess(h, NativeMethods.ProcessIoPriorityClass, ref value, sizeof(int));
        return status == 0
            ? new("IoPriority", ActionStatus.Applied, io.ToString())
            : new("IoPriority", ActionStatus.Failed, $"NTSTATUS 0x{status:X8}");
    }

    public ActionResult SetMemoryPriority(int pid, MemoryPriority memory)
    {
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("MemoryPriority");
        var info = new NativeMethods.MEMORY_PRIORITY_INFORMATION { MemoryPriority = (uint)memory };
        return WithStruct(info, ptr =>
            NativeMethods.SetProcessInformation(h, NativeMethods.ProcessMemoryPriority, ptr, (uint)Marshal.SizeOf(info)))
            ? new("MemoryPriority", ActionStatus.Applied, memory.ToString())
            : Fail("MemoryPriority");
    }

    public ActionResult SetEfficiencyMode(int pid, bool enabled)
    {
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("EfficiencyMode");
        // enabled  -> ControlMask+StateMask = EXECUTION_SPEED  (EcoQoS on)
        // disabled -> ControlMask=StateMask=0                  (reset to system-managed; the documented
        //             "return to default" behavior — Windows then reports it as not-explicitly-set)
        var flag = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
        var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
        {
            Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = enabled ? flag : 0,
            StateMask = enabled ? flag : 0,
        };
        return WithStruct(state, ptr =>
            NativeMethods.SetProcessInformation(h, NativeMethods.ProcessPowerThrottling, ptr, (uint)Marshal.SizeOf(state)))
            ? new("EfficiencyMode", ActionStatus.Applied, enabled ? "on" : "off")
            : Fail("EfficiencyMode");
    }

    public ActionResult SetPriorityBoostDisabled(int pid, bool disabled)
    {
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("PriorityBoost");
        return NativeMethods.SetProcessPriorityBoost(h, disabled)
            ? new("PriorityBoost", ActionStatus.Applied, disabled ? "disabled" : "enabled")
            : Fail("PriorityBoost");
    }

    public ActionResult SetGpuSchedulingPriority(int pid, GpuSchedulingPriority priority)
    {
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("GpuSchedulingPriority");
        var status = NativeMethods.D3DKMTSetProcessSchedulingPriorityClass(h.DangerousGetHandle(), (int)priority);
        return status == 0
            ? new("GpuSchedulingPriority", ActionStatus.Applied, priority.ToString())
            : new("GpuSchedulingPriority", ActionStatus.Failed, $"NTSTATUS 0x{status:X8}");
    }

    public ActionResult SetCpuSets(int pid, CpuSetSelection selection, IReadOnlyList<uint>? customIds)
    {
        if (selection == CpuSetSelection.Unset) return new("CpuSets", ActionStatus.Skipped);
        using var h = Open(pid, NativeMethods.ACCESS_WRITE);
        if (h.IsInvalid) return Fail("CpuSets");

        uint[]? ids = selection switch
        {
            CpuSetSelection.All => null, // null clears the restriction
            CpuSetSelection.PerformanceCores => _topology.PerformanceCoreSetIds(),
            CpuSetSelection.EfficiencyCores => _topology.EfficiencyCoreSetIds(),
            CpuSetSelection.Custom => customIds?.ToArray() ?? Array.Empty<uint>(),
            _ => null,
        };

        if (selection is CpuSetSelection.EfficiencyCores && (ids is null || ids.Length == 0))
            return new("CpuSets", ActionStatus.Unsupported, "no distinct efficiency cores on this CPU");

        var ok = NativeMethods.SetProcessDefaultCpuSets(h, ids, (uint)(ids?.Length ?? 0));
        return ok
            ? new("CpuSets", ActionStatus.Applied, selection == CpuSetSelection.All ? "cleared" : $"{ids!.Length} sets")
            : Fail("CpuSets");
    }

    /// <summary>GPU preference is a persistent per-exe registry setting (needs the full path).</summary>
    public ActionResult SetGpuPreference(string? exePath, GpuPreference preference)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return new("GpuPreference", ActionStatus.Skipped, "exe path unknown");
        try
        {
            _gpuPrefs.Set(exePath!, preference);
            return new("GpuPreference", ActionStatus.Applied, $"{preference} (applies on relaunch)");
        }
        catch (Exception ex)
        {
            return new("GpuPreference", ActionStatus.Failed, ex.Message);
        }
    }

    // ---- reading current per-process state (for menu checkmarks + the rule editor) ----

    /// <summary>A process's current settings that aren't in the live table but drive the menu/editor.</summary>
    public readonly record struct CurrentExtras(
        CpuSetSelection? CpuSets, GpuSchedulingPriority? GpuScheduling, GpuPreference? GpuPreference);

    public CurrentExtras ReadCurrentExtras(int pid, string? exePath) =>
        new(ReadCpuSets(pid), ReadGpuScheduling(pid), ReadGpuPreference(exePath));

    /// <summary>Current default CPU-set assignment mapped back to All / Performance / Efficiency / Custom.</summary>
    public CpuSetSelection? ReadCpuSets(int pid)
    {
        using var h = Open(pid, NativeMethods.ACCESS_READ);
        if (h.IsInvalid) return null;
        // Size probe: with a null/empty buffer this returns false when sets exist, but always fills
        // requiredIdCount (it returns true with count 0 when the process has no default CPU sets).
        NativeMethods.GetProcessDefaultCpuSets(h, null, 0, out var required);
        if (required == 0) return CpuSetSelection.All; // no restriction = all cores
        var ids = new uint[required];
        if (!NativeMethods.GetProcessDefaultCpuSets(h, ids, required, out _)) return null;
        return MapCpuSetIds(ids);
    }

    private CpuSetSelection MapCpuSetIds(uint[] ids)
    {
        var sorted = ids.OrderBy(x => x).ToArray();
        bool Matches(uint[] a) => a.Length == sorted.Length && a.OrderBy(x => x).SequenceEqual(sorted);
        if (Matches(_topology.PerformanceCoreSetIds())) return CpuSetSelection.PerformanceCores;
        if (_topology.IsHybrid && Matches(_topology.EfficiencyCoreSetIds())) return CpuSetSelection.EfficiencyCores;
        return CpuSetSelection.Custom;
    }

    /// <summary>Current GPU scheduling priority class, or null if it can't be read.</summary>
    public GpuSchedulingPriority? ReadGpuScheduling(int pid)
    {
        using var h = Open(pid, NativeMethods.ACCESS_READ);
        if (h.IsInvalid) return null;
        var status = NativeMethods.D3DKMTGetProcessSchedulingPriorityClass(h.DangerousGetHandle(), out var cls);
        return status == 0 && Enum.IsDefined(typeof(GpuSchedulingPriority), cls) ? (GpuSchedulingPriority)cls : null;
    }

    /// <summary>Current per-exe GPU preference from the registry (SystemDefault if none set).</summary>
    public GpuPreference? ReadGpuPreference(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        try { return _gpuPrefs.Get(exePath!); } catch { return null; }
    }

    // ---- one-shot lifecycle actions (not part of a saved rule) ----

    /// <summary>Empty the process working set (RAM trim). Pages are faulted back in on demand.</summary>
    public ActionResult TrimWorkingSet(int pid)
    {
        using var h = Open(pid, NativeMethods.ACCESS_TRIM);
        if (h.IsInvalid) return Fail("TrimMemory");
        return NativeMethods.K32EmptyWorkingSet(h)
            ? new("TrimMemory", ActionStatus.Applied, "working set emptied")
            : Fail("TrimMemory");
    }

    /// <summary>Force-kill the process.</summary>
    public ActionResult Terminate(int pid)
    {
        try { using var p = Process.GetProcessById(pid); p.Kill(); return new("Terminate", ActionStatus.Applied); }
        catch (Exception ex) { return new("Terminate", ActionStatus.Failed, ex.Message); }
    }

    /// <summary>Ask the process to close gracefully (posts WM_CLOSE to its main window).</summary>
    public ActionResult Close(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.CloseMainWindow()
                ? new("Close", ActionStatus.Applied, "close requested")
                : new("Close", ActionStatus.Skipped, "no main window to close");
        }
        catch (Exception ex) { return new("Close", ActionStatus.Failed, ex.Message); }
    }

    /// <summary>Kill the process and relaunch its executable, optionally elevated (UAC prompt).</summary>
    public ActionResult Restart(int pid, string? exePath, bool asAdmin)
    {
        var action = asAdmin ? "RestartAsAdmin" : "Restart";
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return new(action, ActionStatus.Failed, "executable path unknown");
        try
        {
            try { using var p = Process.GetProcessById(pid); p.Kill(); p.WaitForExit(4000); } catch { /* already gone */ }
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
            };
            if (asAdmin) psi.Verb = "runas";
            Process.Start(psi);
            return new(action, ActionStatus.Applied, Path.GetFileName(exePath));
        }
        catch (Exception ex) { return new(action, ActionStatus.Failed, ex.Message); }
    }

    /// <summary>Apply every action a rule specifies to one process; returns per-action results.</summary>
    public IReadOnlyList<ActionResult> ApplyRule(ProcessRule rule, int pid, string? exePath)
    {
        var results = new List<ActionResult>();
        if (rule.CpuPriority is { } cp) results.Add(SetCpuPriority(pid, cp));
        if (rule.AffinityMask is { } am && am != 0) results.Add(SetAffinity(pid, am));
        if (rule.IoPriority is { } io) results.Add(SetIoPriority(pid, io));
        if (rule.MemoryPriority is { } mp) results.Add(SetMemoryPriority(pid, mp));
        if (rule.EfficiencyMode is { } eco) results.Add(SetEfficiencyMode(pid, eco));
        if (rule.DisablePriorityBoost is { } boost) results.Add(SetPriorityBoostDisabled(pid, boost));
        if (rule.GpuSchedulingPriority is { } gsp) results.Add(SetGpuSchedulingPriority(pid, gsp));
        if (rule.CpuSetSelection != CpuSetSelection.Unset) results.Add(SetCpuSets(pid, rule.CpuSetSelection, rule.CpuSetIds));
        if (rule.GpuPreference is { } gp) results.Add(SetGpuPreference(exePath, gp));
        return results;
    }

    private static bool WithStruct<T>(T value, Func<IntPtr, bool> action) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(value, ptr, false); return action(ptr); }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
