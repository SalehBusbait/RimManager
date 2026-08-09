using System.Collections.Immutable;
using RimManager.Core.Abstractions;

namespace RimManager.Core.Diagnostics;

/// <summary>
/// Where every subsystem writes. Kept deliberately narrow so <c>Core</c> can log
/// without knowing whether anything is listening — the file sink and the Activity
/// tab both attach from outside.
/// </summary>
public interface IActivityLog
{
    /// <summary>The minimum level that is recorded. Below it, <see cref="Write"/> is a no-op.</summary>
    LogLevel MinimumLevel { get; set; }

    void Write(LogLevel level, string subsystem, string message);
}

/// <summary>
/// The in-memory activity log behind dock tab <c>2f</c>: a fixed-capacity ring of
/// the most recent entries.
/// <para>
/// A ring rather than a growing list because "10k lines is normal" (<c>2f</c>) and a
/// long session must not turn the log into a memory leak. Dropping the oldest lines
/// is the right trade: the Activity tab exists to explain what just happened, and
/// the on-disk log — which the file sink keeps in full — is what gets pasted into an
/// issue.
/// </para>
/// <para>
/// Pure: no I/O, no UI. Time comes from <see cref="IClock"/>, so tests control
/// timestamps. Thread-safe, because scanning and sorting log from worker threads.
/// </para>
/// </summary>
public sealed class ActivityLog : IActivityLog
{
    /// <summary>Entries retained in memory. Past this, the oldest are dropped.</summary>
    public const int DefaultCapacity = 10_000;

    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries;
    private readonly IClock _clock;
    private readonly int _capacity;

    /// <param name="clock">
    /// Required, not defaulted: nothing in the domain calls <c>DateTimeOffset.UtcNow</c>
    /// directly, which is what keeps timestamps deterministic under test.
    /// </param>
    public ActivityLog(IClock clock, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _clock = clock;
        _capacity = capacity;
        _entries = new Queue<LogEntry>(Math.Min(capacity, 1024));
    }

    /// <summary>Default is <see cref="LogLevel.Info"/>; Settings ▸ Advanced can lower it.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Raised after an entry is recorded, so a sink or the UI can follow along.</summary>
    public event Action<LogEntry>? EntryWritten;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public void Write(LogLevel level, string subsystem, string message)
    {
        if (level < MinimumLevel) return;

        var entry = new LogEntry(_clock.UtcNow, level, subsystem, message);

        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity) _entries.Dequeue();
        }

        // Raised outside the lock: a subscriber that writes to disk must not be able
        // to stall every other thread that wants to log.
        EntryWritten?.Invoke(entry);
    }

    public void Trace(string subsystem, string message) => Write(LogLevel.Trace, subsystem, message);
    public void Debug(string subsystem, string message) => Write(LogLevel.Debug, subsystem, message);
    public void Info(string subsystem, string message) => Write(LogLevel.Info, subsystem, message);
    public void Warn(string subsystem, string message) => Write(LogLevel.Warn, subsystem, message);
    public void Error(string subsystem, string message) => Write(LogLevel.Error, subsystem, message);

    /// <summary>An immutable snapshot, oldest first — safe to hand to the UI thread.</summary>
    public ImmutableArray<LogEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }

    /// <summary>
    /// Snapshot filtered by level and subsystem, which is exactly what the Activity
    /// tab's chips ask for (All / Errors / Warn / Debug).
    /// </summary>
    public ImmutableArray<LogEntry> Snapshot(LogLevel minimum, string? subsystem = null)
    {
        lock (_gate)
        {
            return
            [
                .. _entries.Where(e => e.Level >= minimum
                    && (subsystem is null || string.Equals(e.Subsystem, subsystem, StringComparison.Ordinal)))
            ];
        }
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}

/// <summary>
/// The log that discards everything. Lets a <c>Core</c> service take an
/// <see cref="IActivityLog"/> unconditionally instead of null-checking at every
/// call site, and keeps unit tests free of logging noise.
/// </summary>
public sealed class NullActivityLog : IActivityLog
{
    public static readonly NullActivityLog Instance = new();

    private NullActivityLog() { }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Error;

    public void Write(LogLevel level, string subsystem, string message) { }
}
