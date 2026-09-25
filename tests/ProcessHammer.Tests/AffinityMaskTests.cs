using ProcessHammer.Core.Util;
using Xunit;

namespace ProcessHammer.Tests;

public class AffinityMaskTests
{
    [Theory]
    [InlineData(8, 0xFFUL)]
    [InlineData(4, 0xFUL)]
    [InlineData(32, 0xFFFFFFFFUL)]
    [InlineData(64, ulong.MaxValue)]
    public void All_ProducesFullMask(int cores, ulong expected) =>
        Assert.Equal(expected, AffinityMask.All(cores));

    [Theory]
    [InlineData(0b1UL, 1)]
    [InlineData(0b111UL, 3)]
    [InlineData(0xFFUL, 8)]
    public void CountBits_Works(ulong mask, int expected) =>
        Assert.Equal(expected, AffinityMask.CountBits(mask));

    [Theory]
    [InlineData(0b1UL, "0")]
    [InlineData(0b1110001UL, "0,4-6")]
    [InlineData(0xFFUL, "0-7")]
    public void ToRangeString_Compacts(ulong mask, string expected) =>
        Assert.Equal(expected, AffinityMask.ToRangeString(mask));

    [Theory]
    [InlineData("0-7", 8, 0xFFUL)]
    [InlineData("0,4-6", 32, 0b1110001UL)]
    [InlineData("", 8, 0UL)]
    [InlineData("all", 8, 0UL)]
    public void TryParse_ValidInputs(string text, int cores, ulong expected)
    {
        Assert.True(AffinityMask.TryParseRangeString(text, cores, out var mask));
        Assert.Equal(expected, mask);
    }

    [Theory]
    [InlineData("0-99", 8)]   // out of range
    [InlineData("8", 8)]      // >= logical count
    [InlineData("abc", 8)]    // not a number
    [InlineData("5-2", 8)]    // reversed range
    public void TryParse_RejectsInvalid(string text, int cores) =>
        Assert.False(AffinityMask.TryParseRangeString(text, cores, out _));

    [Fact]
    public void RoundTrip_IndicesToMaskToString()
    {
        var mask = AffinityMask.FromIndices(new[] { 0, 1, 2, 7 });
        Assert.Equal("0-2,7", AffinityMask.ToRangeString(mask));
        Assert.Equal(new[] { 0, 1, 2, 7 }, AffinityMask.ToIndices(mask));
    }
}
