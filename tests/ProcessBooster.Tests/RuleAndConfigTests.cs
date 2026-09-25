using System.IO;
using ProcessBooster.Core.Config;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;
using Xunit;

namespace ProcessBooster.Tests;

public class ProcessRuleTests
{
    [Theory]
    [InlineData("MyGame.exe", "mygame")]
    [InlineData("  Chrome  ", "chrome")]
    [InlineData("notepad", "notepad")]
    [InlineData("Foo.EXE", "foo")]
    public void Normalize_StripsExeAndLowercases(string input, string expected) =>
        Assert.Equal(expected, ProcessRule.Normalize(input));

    [Fact]
    public void HasAnyAction_FalseWhenEmpty() =>
        Assert.False(new ProcessRule { Match = "x" }.HasAnyAction);

    [Fact]
    public void HasAnyAction_TrueWithOneAction() =>
        Assert.True(new ProcessRule { Match = "x", CpuPriority = CpuPriority.High }.HasAnyAction);

    [Fact]
    public void HasAnyAction_IgnoresZeroAffinity() =>
        Assert.False(new ProcessRule { Match = "x", AffinityMask = 0 }.HasAnyAction);
}

public class RuleMatcherTests
{
    private static ProcessRule Rule(string match, bool enabled = true) =>
        new() { Match = match, Enabled = enabled, CpuPriority = CpuPriority.High };

    [Fact]
    public void Matches_ByNameIgnoringExeAndCase() =>
        Assert.True(RuleMatcher.Matches(Rule("MyGame.exe"), "mygame"));

    [Fact]
    public void DoesNotMatch_DisabledRule() =>
        Assert.False(RuleMatcher.Matches(Rule("mygame", enabled: false), "mygame"));

    [Fact]
    public void DoesNotMatch_RuleWithNoActions() =>
        Assert.False(RuleMatcher.Matches(new ProcessRule { Match = "mygame" }, "mygame"));

    [Fact]
    public void FirstMatch_ReturnsFirstEnabledActionableRule()
    {
        var rules = new[] { Rule("chrome"), Rule("mygame"), Rule("mygame") };
        var match = RuleMatcher.FirstMatch(rules, "MyGame");
        Assert.Same(rules[1], match);
    }
}

public class ConfigStoreTests
{
    [Fact]
    public void RoundTrip_PreservesRulesAndEnumsAsNames()
    {
        var config = new AppConfig
        {
            PollSeconds = 7,
            Rules =
            {
                new ProcessRule
                {
                    Match = "MyGame",
                    CpuPriority = CpuPriority.High,
                    IoPriority = IoPriority.Low,
                    MemoryPriority = MemoryPriority.BelowNormal,
                    EfficiencyMode = false,
                    AffinityMask = 0xFF,
                    CpuSetSelection = CpuSetSelection.PerformanceCores,
                    GpuPreference = GpuPreference.HighPerformance,
                }
            }
        };

        var json = ConfigStore.Serialize(config);
        Assert.Contains("\"High\"", json);                 // enum by name, not number
        Assert.Contains("\"PerformanceCores\"", json);

        var back = ConfigStore.Deserialize(json);
        Assert.Equal(7, back.PollSeconds);
        var rule = Assert.Single(back.Rules);
        Assert.Equal(CpuPriority.High, rule.CpuPriority);
        Assert.Equal(IoPriority.Low, rule.IoPriority);
        Assert.Equal(MemoryPriority.BelowNormal, rule.MemoryPriority);
        Assert.False(rule.EfficiencyMode);
        Assert.Equal(0xFFUL, rule.AffinityMask);
        Assert.Equal(GpuPreference.HighPerformance, rule.GpuPreference);
    }

    [Fact]
    public void Deserialize_RejectsNewerSchema()
    {
        var json = "{ \"SchemaVersion\": 9999, \"Rules\": [] }";
        Assert.Throws<InvalidDataException>(() => ConfigStore.Deserialize(json));
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var config = new AppConfig { Rules = { new ProcessRule { Match = "a", CpuPriority = CpuPriority.High } } };
        var clone = config.Clone();
        clone.Rules[0].Match = "b";
        Assert.Equal("a", config.Rules[0].Match); // original untouched
    }

    [Fact]
    public void ImportExport_RoundTripsThroughDisk()
    {
        var config = new AppConfig { PollSeconds = 5, Rules = { new ProcessRule { Match = "x", CpuPriority = CpuPriority.AboveNormal } } };
        var path = Path.Combine(Path.GetTempPath(), $"pb-test-{Guid.NewGuid():N}.json");
        try
        {
            ConfigStore.Export(config, path);
            var imported = ConfigStore.Import(path);
            Assert.Equal(5, imported.PollSeconds);
            Assert.Equal(CpuPriority.AboveNormal, imported.Rules[0].CpuPriority);
        }
        finally { File.Delete(path); }
    }
}
