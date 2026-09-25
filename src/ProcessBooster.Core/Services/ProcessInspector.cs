using System.Diagnostics;
using System.Runtime.InteropServices;
using ProcessBooster.Core.Interop;
using ProcessBooster.Core.Models;

namespace ProcessBooster.Core.Services;

/// <summary>
/// Produces <see cref="ProcessSnapshot"/>s for the live table. Every read is best-effort:
/// protected/inaccessible processes simply come back with null fields instead of throwing.
/// </summary>
public sealed class ProcessInspector
{
    public IReadOnlyList<ProcessSnapshot> Snapshot()
    {
        var list = new List<ProcessSnapshot>();
        foreach (var p in Process.GetProcesses())
        {
            try { list.Add(Read(p)); }
            catch { /* process may have exited mid-scan; ignore */ }
            finally { p.Dispose(); }
        }
        return list;
    }

    public ProcessSnapshot Read(Process p)
    {
        // Working set, thread count and CPU time all come from the single bulk system query that
        // Process.GetProcesses() already made — no per-process syscall. We deliberately do NOT read
        // the exe path here (Process.MainModule enumerates modules and is very slow); it's fetched
        // on demand for the selected row via ReadExePath.
        long ws = 0; int threads = 0; var cpu = TimeSpan.Zero;
        try { ws = p.WorkingSet64; } catch { }
        try { threads = p.Threads.Count; } catch { }
        try { cpu = p.TotalProcessorTime; } catch { }

        CpuPriority? prio = null; ulong? affinity = null;
        IoPriority? io = null; MemoryPriority? mem = null; bool? eco = null; bool? boost = null;

        using (var h = NativeMethods.OpenProcess(NativeMethods.ACCESS_READ, false, p.Id))
        {
            if (!h.IsInvalid)
            {
                var pc = NativeMethods.GetPriorityClass(h);
                if (pc != 0) prio = ProcessController.FromNative(pc);

                if (NativeMethods.GetProcessAffinityMask(h, out var procMask, out _))
                    affinity = (ulong)procMask;

                if (NativeMethods.GetProcessPriorityBoost(h, out var disabled))
                    boost = !disabled;

                mem = ReadMemoryPriority(h);
                eco = ReadEfficiency(h);
                io = ReadIoPriority(h);
            }
        }

        return new ProcessSnapshot
        {
            Pid = p.Id,
            Name = SafeName(p),
            ExePath = null,
            CpuPriority = prio,
            AffinityMask = affinity,
            IoPriority = io,
            MemoryPriority = mem,
            EfficiencyMode = eco,
            PriorityBoostEnabled = boost,
            WorkingSetBytes = ws,
            ThreadCount = threads,
            CpuTime = cpu,
        };
    }

    /// <summary>Full image path for one process (fast; used for the selected row only).</summary>
    public string? ReadExePath(int pid)
    {
        using var h = NativeMethods.OpenProcess(NativeMethods.ACCESS_READ, false, pid);
        if (h.IsInvalid) return null;
        uint cap = 1024;
        var sb = new System.Text.StringBuilder((int)cap);
        return NativeMethods.QueryFullProcessImageName(h, 0, sb, ref cap) ? sb.ToString() : null;
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; } catch { return $"pid {p.Id}"; }
    }

    private static MemoryPriority? ReadMemoryPriority(Microsoft.Win32.SafeHandles.SafeProcessHandle h)
    {
        var info = new NativeMethods.MEMORY_PRIORITY_INFORMATION();
        var size = Marshal.SizeOf(info);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.GetProcessInformation(h, NativeMethods.ProcessMemoryPriority, ptr, (uint)size)) return null;
            info = Marshal.PtrToStructure<NativeMethods.MEMORY_PRIORITY_INFORMATION>(ptr);
            return Enum.IsDefined(typeof(MemoryPriority), (int)info.MemoryPriority) ? (MemoryPriority)info.MemoryPriority : null;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static bool? ReadEfficiency(Microsoft.Win32.SafeHandles.SafeProcessHandle h)
    {
        // Version must be initialized on input — the API reads it to interpret the buffer.
        var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
        {
            Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
        };
        var size = Marshal.SizeOf(state);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(state, ptr, false);
            if (!NativeMethods.GetProcessInformation(h, NativeMethods.ProcessPowerThrottling, ptr, (uint)size)) return null;
            state = Marshal.PtrToStructure<NativeMethods.PROCESS_POWER_THROTTLING_STATE>(ptr);
            if ((state.ControlMask & NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED) == 0)
                return null; // system-managed; not explicitly set
            return (state.StateMask & NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED) != 0;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static IoPriority? ReadIoPriority(Microsoft.Win32.SafeHandles.SafeProcessHandle h)
    {
        var value = 0;
        var status = NativeMethods.NtQueryInformationProcess(h, NativeMethods.ProcessIoPriorityClass, ref value, sizeof(int), IntPtr.Zero);
        if (status != 0) return null;
        return Enum.IsDefined(typeof(IoPriority), value) ? (IoPriority)value : null;
    }
}
