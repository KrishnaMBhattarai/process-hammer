using ProcessBooster.Core.Services;
using Xunit;

namespace ProcessBooster.Tests;

/// <summary>Pure affinity-preset mask logic (P / E / all cores) — no OS or UI.</summary>
public class AffinityPresetsTests
{
    [Theory]
    [InlineData(0, 0UL)]
    [InlineData(1, 0b1UL)]
    [InlineData(4, 0xFUL)]
    [InlineData(8, 0xFFUL)]
    [InlineData(64, ulong.MaxValue)]
    [InlineData(128, ulong.MaxValue)] // clamp: never shift past 64 bits
    public void AllMask_CoversRequestedCores(int count, ulong expected) =>
        Assert.Equal(expected, AffinityPresets.AllMask(count));

    // 4 performance cores (EfficiencyClass 1) on logical 0-3, 4 efficiency cores (class 0) on 4-7.
    private static IReadOnlyList<CpuSetInfo> Hybrid() => new[]
    {
        new CpuSetInfo(0, 0, 0, 1), new CpuSetInfo(1, 1, 1, 1),
        new CpuSetInfo(2, 2, 2, 1), new CpuSetInfo(3, 3, 3, 1),
        new CpuSetInfo(4, 4, 4, 0), new CpuSetInfo(5, 5, 5, 0),
        new CpuSetInfo(6, 6, 6, 0), new CpuSetInfo(7, 7, 7, 0),
    };

    [Fact]
    public void ClassMask_Hybrid_PerformanceCores() =>
        Assert.Equal(0x0FUL, AffinityPresets.ClassMask(Hybrid(), performance: true, 8));

    [Fact]
    public void ClassMask_Hybrid_EfficiencyCores() =>
        Assert.Equal(0xF0UL, AffinityPresets.ClassMask(Hybrid(), performance: false, 8));

    [Fact]
    public void ClassMask_NonHybrid_BothClassesAreAllCores()
    {
        var flat = new[] { new CpuSetInfo(0, 0, 0, 0), new CpuSetInfo(1, 1, 1, 0), new CpuSetInfo(2, 2, 2, 0), new CpuSetInfo(3, 3, 3, 0) };
        Assert.Equal(0xFUL, AffinityPresets.ClassMask(flat, performance: true, 4));
        Assert.Equal(0xFUL, AffinityPresets.ClassMask(flat, performance: false, 4));
    }

    [Fact]
    public void ClassMask_EmptyTopology_FallsBackToAllCores() =>
        Assert.Equal(0xFFUL, AffinityPresets.ClassMask(System.Array.Empty<CpuSetInfo>(), performance: true, 8));

    [Fact]
    public void ClassMask_IgnoresLogicalIndexesBeyond63()
    {
        // The only performance core sits at logical 64 (out of a 64-bit mask) → nothing to set → fall back to all.
        var sets = new[] { new CpuSetInfo(0, 64, 0, 1), new CpuSetInfo(1, 0, 1, 0) };
        Assert.Equal(AffinityPresets.AllMask(8), AffinityPresets.ClassMask(sets, performance: true, 8));
    }

    [Fact]
    public void ClassMask_MixedValidAndOutOfRange_KeepsOnlyValidBits()
    {
        var sets = new[] { new CpuSetInfo(0, 2, 0, 1), new CpuSetInfo(1, 70, 1, 1), new CpuSetInfo(2, 0, 2, 0) };
        Assert.Equal(1UL << 2, AffinityPresets.ClassMask(sets, performance: true, 8));
    }
}
