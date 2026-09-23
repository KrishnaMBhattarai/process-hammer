using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProcessBooster.Core.Interop;

/// <summary>
/// Hand-verified P/Invoke surface. Signatures cross-checked against Microsoft Learn
/// (processthreadsapi.h, winnt.h) and, for the undocumented ProcessIoPriority class, against
/// public reverse-engineered references. Keep this file free of policy — callers own semantics.
/// </summary>
internal static class NativeMethods
{
    // ---- process access rights ----
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_SET_INFORMATION = 0x0200;
    internal const uint PROCESS_SET_LIMITED_INFORMATION = 0x2000;

    internal const uint ACCESS_READ = PROCESS_QUERY_LIMITED_INFORMATION;
    internal const uint ACCESS_WRITE =
        PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_INFORMATION | PROCESS_SET_LIMITED_INFORMATION;

    // ---- priority classes (SetPriorityClass) ----
    internal const uint IDLE_PRIORITY_CLASS = 0x00000040;
    internal const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
    internal const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    internal const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
    internal const uint HIGH_PRIORITY_CLASS = 0x00000080;
    internal const uint REALTIME_PRIORITY_CLASS = 0x00000100;

    // ---- PROCESS_INFORMATION_CLASS members we use ----
    internal const int ProcessMemoryPriority = 0;
    internal const int ProcessPowerThrottling = 4;

    // ---- ProcessIoPriority (NtSetInformationProcess); undocumented information class ----
    internal const int ProcessIoPriorityClass = 33;

    // ---- power throttling flags ----
    internal const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    internal const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORY_PRIORITY_INFORMATION
    {
        public uint MemoryPriority;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    // Fast full-image-path lookup (no module enumeration, unlike Process.MainModule).
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool QueryFullProcessImageName(
        SafeProcessHandle handle, uint flags, System.Text.StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetPriorityClass(SafeProcessHandle handle, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetPriorityClass(SafeProcessHandle handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessAffinityMask(SafeProcessHandle handle, UIntPtr mask);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessAffinityMask(SafeProcessHandle handle, out UIntPtr processMask, out UIntPtr systemMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessPriorityBoost(SafeProcessHandle handle, bool disablePriorityBoost);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessPriorityBoost(SafeProcessHandle handle, out bool disablePriorityBoost);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessInformation(SafeProcessHandle handle, int infoClass, IntPtr info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessInformation(SafeProcessHandle handle, int infoClass, IntPtr info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessDefaultCpuSets(SafeProcessHandle handle, uint[]? cpuSetIds, uint count);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemCpuSetInformation(
        IntPtr information, uint bufferLength, out uint returnedLength, SafeProcessHandle process, uint flags);

    // ntdll: the only reliable way to set process I/O priority.
    [DllImport("ntdll.dll")]
    internal static extern int NtSetInformationProcess(SafeProcessHandle handle, int infoClass, ref int info, int length);

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryInformationProcess(SafeProcessHandle handle, int infoClass, ref int info, int length, IntPtr returnLength);

    // gdi32: GPU scheduling priority class.
    [StructLayout(LayoutKind.Sequential)]
    internal struct D3DKMT_SETPROCESSSCHEDULINGPRIORITYCLASS
    {
        public IntPtr hProcess;
        public int PriorityClass;
    }

    [DllImport("gdi32.dll")]
    internal static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr hProcess, int priorityClass);
}
