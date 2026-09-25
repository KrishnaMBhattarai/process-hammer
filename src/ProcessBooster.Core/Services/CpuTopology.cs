using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ProcessBooster.Core.Interop;

namespace ProcessBooster.Core.Services;

/// <summary>One CPU Set as reported by the OS.</summary>
public readonly record struct CpuSetInfo(uint Id, int LogicalProcessorIndex, int CoreIndex, int EfficiencyClass);

/// <summary>
/// Enumerates the machine's CPU Sets via GetSystemCpuSetInformation and classifies them into
/// performance (P) vs efficiency (E) cores using the reported EfficiencyClass. Cached after first read.
/// </summary>
public sealed class CpuTopology
{
    private readonly Lazy<IReadOnlyList<CpuSetInfo>> _sets;

    public CpuTopology() => _sets = new(Query);

    /// <summary>Test/DI seam: build a topology from a known set list instead of querying the OS.</summary>
    public CpuTopology(IReadOnlyList<CpuSetInfo> sets) => _sets = new(() => sets);

    public IReadOnlyList<CpuSetInfo> Sets => _sets.Value;

    public bool IsHybrid => Sets.Select(s => s.EfficiencyClass).Distinct().Count() > 1;

    /// <summary>CPU-set IDs for performance cores (highest EfficiencyClass). All sets if non-hybrid.</summary>
    public uint[] PerformanceCoreSetIds()
    {
        if (Sets.Count == 0) return Array.Empty<uint>();
        var max = Sets.Max(s => s.EfficiencyClass);
        return Sets.Where(s => s.EfficiencyClass == max).Select(s => s.Id).ToArray();
    }

    /// <summary>CPU-set IDs for efficiency cores (lowest EfficiencyClass). Empty if non-hybrid.</summary>
    public uint[] EfficiencyCoreSetIds()
    {
        if (!IsHybrid) return Array.Empty<uint>();
        var min = Sets.Min(s => s.EfficiencyClass);
        return Sets.Where(s => s.EfficiencyClass == min).Select(s => s.Id).ToArray();
    }

    private static IReadOnlyList<CpuSetInfo> Query()
    {
        using var self = new SafeProcessHandle(IntPtr.Zero, ownsHandle: false); // NULL process = system view
        NativeMethods.GetSystemCpuSetInformation(IntPtr.Zero, 0, out var needed, self, 0);
        if (needed == 0) return Array.Empty<CpuSetInfo>();

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.GetSystemCpuSetInformation(buffer, needed, out var written, self, 0))
                return Array.Empty<CpuSetInfo>();

            var result = new List<CpuSetInfo>();
            var offset = 0;
            while (offset + 8 <= (int)written)
            {
                var size = Marshal.ReadInt32(buffer, offset);
                var type = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0) break;
                if (type == 0) // CpuSetInformation
                {
                    var id = (uint)Marshal.ReadInt32(buffer, offset + 8);
                    int logical = Marshal.ReadByte(buffer, offset + 14);
                    int core = Marshal.ReadByte(buffer, offset + 15);
                    int eff = Marshal.ReadByte(buffer, offset + 18);
                    result.Add(new CpuSetInfo(id, logical, core, eff));
                }
                offset += size;
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
