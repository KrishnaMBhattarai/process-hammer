using System.IO;
using ProcessHammer.Core.Logging;
using Xunit;

namespace ProcessHammer.Tests;

public class ActionLogTests
{
    [Fact]
    public void Recent_KeepsOnlyTheLastCapacityEntries()
    {
        var log = new ActionLog(filePath: null, capacity: 3);
        for (var i = 0; i < 10; i++) log.Info($"m{i}");

        var recent = log.Recent().ToArray();
        Assert.Equal(3, recent.Length);
        Assert.Equal(new[] { "m7", "m8", "m9" }, recent.Select(e => e.Message));
    }

    [Theory]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Action)]
    [InlineData(LogLevel.Warn)]
    [InlineData(LogLevel.Error)]
    public void EachLevelHelper_RoutesToMatchingLevel(LogLevel level)
    {
        var log = new ActionLog();
        LogEntry? seen = null;
        log.Logged += e => seen = e;

        switch (level)
        {
            case LogLevel.Info: log.Info("x"); break;
            case LogLevel.Action: log.Action("x"); break;
            case LogLevel.Warn: log.Warn("x"); break;
            case LogLevel.Error: log.Error("x"); break;
        }

        Assert.NotNull(seen);
        Assert.Equal(level, seen!.Value.Level);
        Assert.Equal("x", seen.Value.Message);
    }

    [Fact]
    public void Logged_FiresOncePerWrite()
    {
        var log = new ActionLog();
        var count = 0;
        log.Logged += _ => count++;

        log.Info("a");
        log.Warn("b");

        Assert.Equal(2, count);
    }

    [Fact]
    public void FileSink_WritesFormattedLines()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var log = new ActionLog(path);
            log.Action("hello");

            var text = File.ReadAllText(path);
            Assert.Contains("[Action]", text);
            Assert.Contains("hello", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FileSink_NeverThrows_WhenPathIsUnwritable()
    {
        // A directory path can't be written as a file — logging must swallow the error, not throw.
        var log = new ActionLog(Path.GetTempPath());
        var fired = false;
        log.Logged += _ => fired = true;

        var ex = Record.Exception(() => log.Error("boom"));

        Assert.Null(ex);
        Assert.True(fired);                         // in-memory + event path still work
        Assert.Single(log.Recent());
    }

    [Fact]
    public void LogEntry_ToString_BracketsTheLevel()
    {
        var entry = new LogEntry(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero), LogLevel.Warn, "msg");
        Assert.Contains("[Warn]", entry.ToString());
        Assert.Contains("msg", entry.ToString());
    }
}
