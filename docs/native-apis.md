# Native APIs used by Process Booster

Every setting maps to a documented (or well-established) Windows API. Signatures live in
`ProcessBooster.Core/Interop/NativeMethods.cs`; this table is the reference and rationale.

| Feature | API / mechanism | Values | Notes |
|---|---|---|---|
| CPU priority | `SetPriorityClass` / `GetPriorityClass` (kernel32) | IDLE, BELOW_NORMAL, NORMAL, ABOVE_NORMAL, HIGH, REALTIME | REALTIME needs elevation. |
| CPU affinity | `SetProcessAffinityMask` / `GetProcessAffinityMask` (kernel32) | 64-bit bitmask | Bit *i* = logical processor *i*. |
| Priority boost | `SetProcessPriorityBoost` / `GetProcessPriorityBoost` (kernel32) | on / off | The flag is *disable* boost. |
| I/O priority | `NtSetInformationProcess` / `NtQueryInformationProcess`, class `ProcessIoPriority` (33) (ntdll) | 0 VeryLow, 1 Low, 2 Normal | 3 (High) / 4 (Critical) are system-reserved; user mode can only set 0–2. |
| Memory priority | `SetProcessInformation(ProcessMemoryPriority)` + `MEMORY_PRIORITY_INFORMATION` (kernel32) | 1 VeryLow … 5 Normal | Influences working-set trim order. |
| Efficiency mode | `SetProcessInformation(ProcessPowerThrottling)` + `PROCESS_POWER_THROTTLING_STATE` (kernel32) | EXECUTION_SPEED on/off | On = EcoQoS. Off = reset to system-managed (reads back as "not set"). |
| CPU sets | `GetSystemCpuSetInformation` → IDs → `SetProcessDefaultCpuSets` (kernel32) | set IDs | `EfficiencyClass` distinguishes P-cores (higher) from E-cores (lower). `null` clears. |
| GPU preference | Registry `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, value = exe path, data `GpuPreference=N;` | 0 default, 1 power-saving, 2 high-performance | Persistent; applies on next launch. Other tokens (e.g. `AutoHDREnable`) are preserved. |
| GPU scheduling priority | `D3DKMTSetProcessSchedulingPriorityClass` (gdi32) | Idle…Realtime (0–5) | Best-effort; may require elevation. |

## References

- [SetProcessInformation](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation) · [PROCESS_POWER_THROTTLING_STATE](https://learn.microsoft.com/windows/win32/api/processthreadsapi/ns-processthreadsapi-process_power_throttling_state) · [MEMORY_PRIORITY_INFORMATION](https://learn.microsoft.com/windows/win32/api/processthreadsapi/ns-processthreadsapi-memory_priority_information)
- [Quality of Service (EcoQoS)](https://learn.microsoft.com/windows/win32/procthread/quality-of-service)
- [CPU Sets](https://learn.microsoft.com/windows/win32/procthread/cpu-sets) · [GetSystemCpuSetInformation](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemcpusetinformation) · [SetProcessDefaultCpuSets](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessdefaultcpusets)
- [IO_PRIORITY_HINT](https://learn.microsoft.com/windows-hardware/drivers/ddi/wdm/ne-wdm-_io_priority_hint) (I/O priority levels)
- GPU preference registry format: community-documented (elevenforum, ninjaone) — `GpuPreference=2;` = high-performance dGPU.
