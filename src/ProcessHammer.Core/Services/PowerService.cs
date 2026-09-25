using System.Runtime.InteropServices;
using System.Text;
using ProcessHammer.Core.Interop;

namespace ProcessHammer.Core.Services;

/// <summary>One Windows power plan (scheme): its GUID and friendly name.</summary>
public readonly record struct PowerScheme(Guid Guid, string Name);

/// <summary>
/// Lists the machine's power plans and switches the active one — the gaming-relevant knob
/// (e.g. flip to "High performance" while boosting). Thin, allocation-light wrapper over powrprof.
/// </summary>
public sealed class PowerService
{
    /// <summary>All power schemes on this machine, in enumeration order.</summary>
    public IReadOnlyList<PowerScheme> ListSchemes()
    {
        var list = new List<PowerScheme>();
        for (uint index = 0; ; index++)
        {
            var guid = Guid.Empty;
            var size = (uint)Marshal.SizeOf<Guid>();
            var ret = NativeMethods.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.ACCESS_SCHEME, index, ref guid, ref size);
            if (ret != 0) break; // ERROR_NO_MORE_ITEMS (259) ends the loop
            list.Add(new PowerScheme(guid, ReadFriendlyName(guid)));
        }
        return list;
    }

    /// <summary>GUID of the currently active power scheme, or null if it can't be read.</summary>
    public Guid? GetActiveScheme()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { NativeMethods.LocalFree(ptr); }
    }

    /// <summary>Make <paramref name="guid"/> the active power scheme. Returns true on success.</summary>
    public bool SetActiveScheme(Guid guid) =>
        NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref guid) == 0;

    private static string ReadFriendlyName(Guid guid)
    {
        uint size = 0;
        NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0) return guid.ToString();
        var buffer = new byte[size];
        return NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : guid.ToString();
    }
}
