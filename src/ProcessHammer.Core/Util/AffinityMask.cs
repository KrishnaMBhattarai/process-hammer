namespace ProcessHammer.Core.Util;

/// <summary>
/// Pure helpers for turning CPU-affinity masks to/from human strings like "0-7,16".
/// No Windows calls here, so this is fully unit-testable on any platform.
/// </summary>
public static class AffinityMask
{
    /// <summary>All-cores mask for the given logical processor count (e.g. 8 → 0xFF).</summary>
    public static ulong All(int logicalProcessors) =>
        logicalProcessors is <= 0 or > 64 ? ulong.MaxValue : (ulong.MaxValue >> (64 - logicalProcessors));

    /// <summary>Enumerate the zero-based core indices set in a mask.</summary>
    public static IEnumerable<int> ToIndices(ulong mask)
    {
        for (var i = 0; i < 64; i++)
            if ((mask & (1UL << i)) != 0)
                yield return i;
    }

    public static int CountBits(ulong mask)
    {
        var count = 0;
        while (mask != 0) { count += (int)(mask & 1); mask >>= 1; }
        return count;
    }

    /// <summary>Build a mask from core indices, e.g. [0,1,2] → 0b111.</summary>
    public static ulong FromIndices(IEnumerable<int> indices)
    {
        ulong mask = 0;
        foreach (var i in indices)
            if (i is >= 0 and < 64)
                mask |= 1UL << i;
        return mask;
    }

    /// <summary>Format a mask as a compact range string, e.g. 0b1110001 → "0,4-6".</summary>
    public static string ToRangeString(ulong mask)
    {
        var indices = ToIndices(mask).ToList();
        if (indices.Count == 0) return "";
        var parts = new List<string>();
        var start = indices[0];
        var prev = indices[0];
        for (var k = 1; k <= indices.Count; k++)
        {
            if (k < indices.Count && indices[k] == prev + 1) { prev = indices[k]; continue; }
            parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
            if (k < indices.Count) { start = indices[k]; prev = indices[k]; }
        }
        return string.Join(",", parts);
    }

    /// <summary>
    /// Parse a range string like "0-7,16" (or "all"/empty) into a mask.
    /// Returns false on malformed input.
    /// </summary>
    public static bool TryParseRangeString(string? text, int logicalProcessors, out ulong mask)
    {
        mask = 0;
        if (string.IsNullOrWhiteSpace(text)) { mask = 0; return true; } // empty = "no restriction"
        var t = text.Trim();
        if (t.Equals("all", StringComparison.OrdinalIgnoreCase)) { mask = 0; return true; }

        ulong result = 0;
        foreach (var rawPart in t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = rawPart.IndexOf('-');
            if (dash < 0)
            {
                if (!int.TryParse(rawPart, out var single) || single is < 0 or >= 64) return false;
                if (single >= logicalProcessors) return false;
                result |= 1UL << single;
            }
            else
            {
                var loText = rawPart[..dash].Trim();
                var hiText = rawPart[(dash + 1)..].Trim();
                if (!int.TryParse(loText, out var lo) || !int.TryParse(hiText, out var hi)) return false;
                if (lo < 0 || hi >= 64 || hi < lo || hi >= logicalProcessors) return false;
                for (var i = lo; i <= hi; i++) result |= 1UL << i;
            }
        }
        mask = result;
        return true;
    }
}
