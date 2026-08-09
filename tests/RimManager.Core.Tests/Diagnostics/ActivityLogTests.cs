using FluentAssertions;
using RimManager.Core.Diagnostics;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Diagnostics;

/// <summary>
/// The activity log behind dock tab <c>2f</c> and Settings ▸ Advanced.
/// </summary>
public sealed class ActivityLogTests
{
    private static (ActivityLog log, FixedClock clock) Build(int capacity = 10)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 27, 12, 6, 11, 204, TimeSpan.Zero));
        return (new ActivityLog(clock, capacity), clock);
    }

    [Fact]
    public void Records_entries_oldest_first()
    {
        var (log, _) = Build();

        log.Info(LogSubsystem.Sort, "first");
        log.Warn(LogSubsystem.Scan, "second");

        log.Snapshot().Select(e => e.Message).Should().Equal("first", "second");
    }

    [Fact]
    public void Stamps_entries_from_the_clock()
    {
        var (log, clock) = Build();

        log.Info(LogSubsystem.Sort, "at noon");
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        log.Info(LogSubsystem.Sort, "later");

        var entries = log.Snapshot();
        (entries[1].Timestamp - entries[0].Timestamp).Should().Be(TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// "10k lines is normal" (2f), so the ring must bound memory. Dropping the oldest
    /// is the right trade — the panel explains what just happened; the on-disk log
    /// keeps the full record.
    /// </summary>
    [Fact]
    public void Drops_the_oldest_entries_past_capacity()
    {
        var (log, _) = Build(capacity: 3);

        for (var i = 1; i <= 5; i++) log.Info(LogSubsystem.Io, $"line {i}");

        log.Count.Should().Be(3);
        log.Snapshot().Select(e => e.Message).Should().Equal("line 3", "line 4", "line 5");
    }

    [Fact]
    public void Entries_below_the_minimum_level_are_not_recorded()
    {
        var (log, _) = Build();
        log.MinimumLevel = LogLevel.Warn;

        log.Debug(LogSubsystem.Sort, "noise");
        log.Info(LogSubsystem.Sort, "also noise");
        log.Warn(LogSubsystem.Sort, "kept");
        log.Error(LogSubsystem.Sort, "kept too");

        log.Snapshot().Select(e => e.Message).Should().Equal("kept", "kept too");
    }

    [Fact]
    public void Lowering_the_minimum_level_takes_effect_immediately()
    {
        var (log, _) = Build();
        log.MinimumLevel = LogLevel.Error;
        log.Debug(LogSubsystem.Sort, "dropped");

        log.MinimumLevel = LogLevel.Trace;
        log.Debug(LogSubsystem.Sort, "kept");

        log.Snapshot().Select(e => e.Message).Should().Equal("kept");
    }

    // --- the Activity tab's filter chips -------------------------------------

    [Fact]
    public void Snapshot_filters_by_level()
    {
        var (log, _) = Build();
        log.MinimumLevel = LogLevel.Trace;

        log.Debug(LogSubsystem.Sort, "d");
        log.Info(LogSubsystem.Sort, "i");
        log.Warn(LogSubsystem.Sort, "w");
        log.Error(LogSubsystem.Sort, "e");

        log.Snapshot(LogLevel.Warn).Select(x => x.Message).Should().Equal("w", "e");
    }

    [Fact]
    public void Snapshot_filters_by_subsystem()
    {
        var (log, _) = Build();

        log.Info(LogSubsystem.Sort, "sorting");
        log.Info(LogSubsystem.Steam, "downloading");
        log.Info(LogSubsystem.Sort, "sorted");

        log.Snapshot(LogLevel.Trace, LogSubsystem.Sort)
            .Select(x => x.Message).Should().Equal("sorting", "sorted");
    }

    [Fact]
    public void Raises_an_event_per_recorded_entry()
    {
        var (log, _) = Build();
        var seen = new List<string>();
        log.EntryWritten += e => seen.Add(e.Message);

        log.MinimumLevel = LogLevel.Info;
        log.Debug(LogSubsystem.Ui, "filtered out");
        log.Info(LogSubsystem.Ui, "recorded");

        // A filtered entry must not reach the sink either.
        seen.Should().Equal("recorded");
    }

    [Fact]
    public void Clear_empties_the_ring()
    {
        var (log, _) = Build();
        log.Info(LogSubsystem.Io, "x");

        log.Clear();

        log.Count.Should().Be(0);
    }

    [Fact]
    public void Capacity_must_be_positive()
    {
        var act = () => new ActivityLog(new FixedClock(DateTimeOffset.UnixEpoch), capacity: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Null_log_discards_everything()
    {
        var act = () => NullActivityLog.Instance.Write(LogLevel.Error, LogSubsystem.Io, "boom");
        act.Should().NotThrow();
    }

    /// <summary>Scanning and sorting log from worker threads, so writes must not tear.</summary>
    [Fact]
    public void Concurrent_writes_do_not_lose_or_corrupt_entries()
    {
        var log = new ActivityLog(new FixedClock(DateTimeOffset.UnixEpoch), capacity: 10_000);

        Parallel.For(0, 500, i => log.Info(LogSubsystem.Scan, $"entry {i}"));

        log.Count.Should().Be(500);
        log.Snapshot().Select(e => e.Message).Distinct().Should().HaveCount(500);
    }

    // --- formatting ---------------------------------------------------------

    /// <summary>
    /// The panel and the on-disk log share one formatter because 2f says the panel
    /// "is the same text as the on-disk log" — its job is to be pasted into an issue.
    /// </summary>
    [Fact]
    public void Formats_as_time_level_subsystem_message()
    {
        var entry = new LogEntry(
            new DateTimeOffset(2026, 7, 27, 12, 6, 11, 204, TimeSpan.Zero),
            LogLevel.Info,
            LogSubsystem.Sort,
            "Topological sort started · 214 nodes, 1,880 edges");

        LogEntryFormatter.Format(entry)
            .Should().Be("12:06:11.204 INFO  sort Topological sort started · 214 nodes, 1,880 edges");
    }

    [Fact]
    public void Level_column_is_padded_so_columns_line_up()
    {
        var at = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        var warn = LogEntryFormatter.Format(new LogEntry(at, LogLevel.Warn, "sort", "x"));
        var error = LogEntryFormatter.Format(new LogEntry(at, LogLevel.Error, "sort", "x"));

        warn.IndexOf("sort", StringComparison.Ordinal)
            .Should().Be(error.IndexOf("sort", StringComparison.Ordinal));
    }

    [Fact]
    public void Formats_a_run_one_entry_per_line()
    {
        var at = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var text = LogEntryFormatter.Format(
        [
            new LogEntry(at, LogLevel.Info, "sort", "one"),
            new LogEntry(at, LogLevel.Error, "steam", "two"),
        ]);

        text.Split(Environment.NewLine).Should().HaveCount(2);
    }

    [Fact]
    public void Subsystem_vocabulary_is_closed_and_matches_the_design()
        => LogSubsystem.All.Should().Equal("sort", "valid", "scan", "steam", "git", "io", "rules", "ui");
}
