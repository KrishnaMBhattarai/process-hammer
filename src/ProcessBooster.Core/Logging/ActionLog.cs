using System.Collections.Concurrent;

namespace ProcessBooster.Core.Logging;

public enum LogLevel { Info, Action, Warn, Error }

public readonly record struct LogEntry(DateTimeOffset Time, LogLevel Level, string Message)
{
    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss}  [{Level}]  {Message}";
}

/// <summary>
/// Thread-safe log with a bounded in-memory ring (for the GUI log viewer) and optional file sink.
/// Raises <see cref="Logged"/> so the UI can append live.
/// </summary>
public sealed class ActionLog
{
    private readonly ConcurrentQueue<LogEntry> _ring = new();
    private readonly int _capacity;
    private readonly string? _filePath;
    private readonly object _fileLock = new();

    public ActionLog(string? filePath = null, int capacity = 2000)
    {
        _filePath = filePath;
        _capacity = capacity;
    }

    public event Action<LogEntry>? Logged;

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Action(string message) => Write(LogLevel.Action, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    public IReadOnlyCollection<LogEntry> Recent() => _ring.ToArray();

    private void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        _ring.Enqueue(entry);
        while (_ring.Count > _capacity && _ring.TryDequeue(out _)) { }

        if (_filePath is not null)
        {
            try
            {
                lock (_fileLock) File.AppendAllText(_filePath, entry + Environment.NewLine);
            }
            catch { /* logging must never throw into callers */ }
        }

        Logged?.Invoke(entry);
    }
}
