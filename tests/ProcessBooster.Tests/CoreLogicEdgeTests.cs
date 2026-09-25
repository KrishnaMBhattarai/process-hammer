using System.IO;
using ProcessBooster.Core.Config;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;
using ProcessBooster.Core.Util;
using Xunit;

namespace ProcessBooster.Tests;

public class AffinityMaskEdgeTests
{
    [Theory]
    [InlineData(" 0 - 3 , 5 ", 8, 0b101111UL)] // whitespace around ranges and values
    [InlineData("0,,2", 8, 0b101UL)]            // stray comma → empty part dropped
    [InlineData("3-3", 8, 0b1000UL)]            // single-element range
    [InlineData("   ", 8, 0UL)]                 // whitespace-only = no restriction
    [InlineData(null, 8, 0UL)]                  // null = no restriction
    public void TryParse_HandlesWhitespaceAndEmptyParts(string? text, int cores, ulong expected)
    {
        Assert.True(AffinityMask.TryParseRangeString(text, cores, out var mask));
        Assert.Equal(expected, mask);
    }

    [Theory]
    [InlineData("0-64", 100)]  // hi >= 64 rejected even when cores allow it
    [InlineData("64", 100)]    // single index >= 64 rejected
    [InlineData("5-2", 8)]     // reversed range
    [InlineData("8", 8)]       // index == core count (out of range)
    [InlineData("abc", 8)]     // non-numeric
    public void TryParse_RejectsInvalid(string text, int cores) =>
        Assert.False(AffinityMask.TryParseRangeString(text, cores, out _));

    [Theory]
    [InlineData(0, ulong.MaxValue)]   // <= 0 guard
    [InlineData(-1, ulong.MaxValue)]
    [InlineData(65, ulong.MaxValue)]  // > 64 guard
    [InlineData(8, 0xFFUL)]
    public void All_EdgeCounts(int cores, ulong expected) =>
        Assert.Equal(expected, AffinityMask.All(cores));

    [Fact]
    public void ToRangeString_EmptyMaskIsEmptyString() => Assert.Equal("", AffinityMask.ToRangeString(0));

    [Fact]
    public void ToRangeString_HandlesGapsBetweenRuns() =>
        Assert.Equal("0-1,8", AffinityMask.ToRangeString(0b100000011));

    [Fact]
    public void FromIndices_IgnoresOutOfRange() =>
        Assert.Equal(0b101UL, AffinityMask.FromIndices(new[] { 0, 2, 64, -1 }));
}

public class ProcessRuleEdgeTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(".exe", "")]
    [InlineData("exexe", "exexe")]        // only a trailing ".exe" is stripped
    [InlineData("my.exe.exe", "my.exe")]  // stripped once
    [InlineData("a.b.exe", "a.b")]
    [InlineData("file.com", "file.com")]  // other extensions kept
    public void Normalize_EdgeInputs(string? input, string expected) =>
        Assert.Equal(expected, ProcessRule.Normalize(input!));

    [Fact]
    public void HasAnyAction_TrueForEachActionInIsolation()
    {
        Assert.True(new ProcessRule { CpuPriority = CpuPriority.High }.HasAnyAction);
        Assert.True(new ProcessRule { AffinityMask = 0b1 }.HasAnyAction);
        Assert.True(new ProcessRule { IoPriority = IoPriority.Low }.HasAnyAction);
        Assert.True(new ProcessRule { MemoryPriority = MemoryPriority.Low }.HasAnyAction);
        Assert.True(new ProcessRule { EfficiencyMode = false }.HasAnyAction);       // explicit false still counts
        Assert.True(new ProcessRule { DisablePriorityBoost = false }.HasAnyAction);
        Assert.True(new ProcessRule { GpuPreference = GpuPreference.HighPerformance }.HasAnyAction);
        Assert.True(new ProcessRule { GpuSchedulingPriority = GpuSchedulingPriority.High }.HasAnyAction);
        Assert.True(new ProcessRule { CpuSetSelection = CpuSetSelection.PerformanceCores }.HasAnyAction);
    }

    [Fact]
    public void HasAnyAction_FalseForEmptyOrZeroAffinity()
    {
        Assert.False(new ProcessRule().HasAnyAction);
        Assert.False(new ProcessRule { AffinityMask = 0 }.HasAnyAction);
        Assert.False(new ProcessRule { CpuSetSelection = CpuSetSelection.Unset }.HasAnyAction);
    }
}

public class RuleMatcherEdgeTests
{
    private static ProcessRule R(string match, bool enabled = true, CpuPriority? cpu = CpuPriority.High) =>
        new() { Match = match, Enabled = enabled, CpuPriority = cpu };

    [Fact]
    public void FirstMatch_NormalizesTheQueriedName()
    {
        var rules = new List<ProcessRule> { R("mygame") };
        Assert.NotNull(RuleMatcher.FirstMatch(rules, "  MYGAME.EXE "));
    }

    [Fact]
    public void FirstMatch_SkipsDisabledEarlierRuleAndReturnsEnabledOne()
    {
        var disabled = R("mygame", enabled: false);
        var enabled = R("mygame");
        var match = RuleMatcher.FirstMatch(new List<ProcessRule> { disabled, enabled }, "mygame");
        Assert.Same(enabled, match);
    }

    [Fact]
    public void FirstMatch_ReturnsNullWhenNothingMatches() =>
        Assert.Null(RuleMatcher.FirstMatch(new List<ProcessRule> { R("chrome") }, "mygame"));

    [Fact]
    public void FirstMatch_SkipsRulesWithNoAction()
    {
        var noAction = R("mygame", cpu: null); // nothing set → not actionable
        Assert.Null(RuleMatcher.FirstMatch(new List<ProcessRule> { noAction }, "mygame"));
    }
}

public class ConfigStoreEdgeTests
{
    [Fact]
    public void Serialize_OmitsNullActions()
    {
        var json = ConfigStore.Serialize(new AppConfig { Rules = { new ProcessRule { Match = "x", CpuPriority = CpuPriority.High } } });
        Assert.DoesNotContain("\"IoPriority\"", json);
        Assert.DoesNotContain("\"MemoryPriority\"", json);
        Assert.Contains("\"CpuPriority\"", json);
    }

    [Fact]
    public void Deserialize_NullLiteral_Throws() =>
        Assert.Throws<System.IO.InvalidDataException>(() => ConfigStore.Deserialize("null"));

    [Fact]
    public void Deserialize_MissingSchemaVersion_DefaultsToCurrent()
    {
        var config = ConfigStore.Deserialize("{\"Rules\":[]}");
        Assert.Equal(AppConfig.CurrentSchemaVersion, config.SchemaVersion);
    }

    [Fact]
    public void Deserialize_CurrentSchema_Loads() =>
        Assert.NotNull(ConfigStore.Deserialize("{\"SchemaVersion\":1,\"Rules\":[]}"));

    [Fact]
    public void SaveThenLoad_RoundTripsToDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            new ConfigStore(path).Save(new AppConfig
            {
                PollSeconds = 9,
                Rules = { new ProcessRule { Match = "mygame", CpuPriority = CpuPriority.High } },
            });
            var loaded = new ConfigStore(path).Load();
            Assert.Equal(9, loaded.PollSeconds);
            Assert.Equal("mygame", Assert.Single(loaded.Rules).Match);
            Assert.Equal(CpuPriority.High, loaded.Rules[0].CpuPriority);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyConfig() =>
        Assert.Empty(new ConfigStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".none")).Load().Rules);

    [Fact]
    public void DefaultPath_IsUnderProcessBoosterFolder() =>
        Assert.Contains("ProcessBooster", ConfigStore.DefaultPath());

    [Fact]
    public void ImportExport_RoundTripsThroughAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            ConfigStore.Export(new AppConfig { Rules = { new ProcessRule { Match = "x", IoPriority = IoPriority.Low } } }, path);
            var imported = ConfigStore.Import(path);
            Assert.Equal(IoPriority.Low, imported.Rules[0].IoPriority);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void RoundTrip_PreservesCustomCpuSetIdsAndNote()
    {
        var config = new AppConfig
        {
            Rules =
            {
                new ProcessRule
                {
                    Match = "mygame",
                    Note = "keep me",
                    CpuSetSelection = CpuSetSelection.Custom,
                    CpuSetIds = new List<uint> { 3, 7, 11 },
                },
            },
        };
        var back = ConfigStore.Deserialize(ConfigStore.Serialize(config)).Rules[0];
        Assert.Equal("keep me", back.Note);
        Assert.Equal(CpuSetSelection.Custom, back.CpuSetSelection);
        Assert.Equal(new List<uint> { 3, 7, 11 }, back.CpuSetIds);
    }
}

public class PriorityMapEdgeTests
{
    [Fact]
    public void FromNative_UnknownValue_ReturnsNull() => Assert.Null(ProcessController.FromNative(0xDEAD));
}
