using FluentAssertions;
using RimManager.Core.Diagnostics;
using RimManager.Storage;
using RimManager.Storage.Diagnostics;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// The on-disk half of the activity log. Real files, because the property that
/// matters — that the panel text and the file text are the same — is only worth
/// anything if it holds against a real write.
/// </summary>
public sealed class FileLogSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rimmanager-log-" + Guid.NewGuid().ToString("N")[..8]);

    private string LogPath => Path.Combine(_dir, "rimmanager.log");

    public FileLogSinkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static ActivityLog NewLog() =>
        new(new SystemClock(), capacity: 100);

    /// <summary>
    /// Reads the log while the sink still holds it open. The sink writes with
    /// FileShare.ReadWrite precisely so the file can be tailed or opened in an editor
    /// mid-session; File.ReadAllLines asks for exclusive-ish sharing and would fail.
    /// </summary>
    private static string[] ReadLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Writes_each_entry_as_one_line()
    {
        var log = NewLog();
        using var sink = new FileLogSink(log, LogPath);

        log.Info(LogSubsystem.Sort, "Topological sort started");
        log.Warn(LogSubsystem.Scan, "Roads of the Rim: About.xml declares no 1.6 support");

        var lines = ReadLog(LogPath);

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("INFO").And.Contain("sort").And.EndWith("Topological sort started");
        lines[1].Should().Contain("WARN").And.Contain("scan");
    }

    /// <summary>
    /// 2f: the Activity panel "is the same text as the on-disk log because its job is
    /// to be pasted into a GitHub issue". One formatter, so the two cannot drift.
    /// </summary>
    [Fact]
    public void On_disk_text_is_byte_identical_to_what_the_panel_shows()
    {
        var log = NewLog();
        using var sink = new FileLogSink(log, LogPath);

        log.Info(LogSubsystem.Steam, "SteamCMD login anonymous · workshop_download_item 294100");
        log.Error(LogSubsystem.Steam, "Item 3011452188 unavailable (hidden by author) — skipped");

        var fromPanel = LogEntryFormatter.Format(log.Snapshot());
        var fromDisk = string.Join(Environment.NewLine, ReadLog(LogPath));

        fromDisk.Should().Be(fromPanel);
    }

    /// <summary>Startup logs before the sink can attach; those lines must not be lost.</summary>
    [Fact]
    public void Backfills_entries_written_before_the_sink_attached()
    {
        var log = NewLog();
        log.Info(LogSubsystem.Io, "before the sink existed");

        using var sink = new FileLogSink(log, LogPath);

        ReadLog(LogPath).Should().ContainSingle().Which.Should().Contain("before the sink existed");
    }

    [Fact]
    public void Appends_across_sessions_rather_than_truncating()
    {
        var first = NewLog();
        using (var sink = new FileLogSink(first, LogPath)) first.Info(LogSubsystem.Io, "session one");

        var second = NewLog();
        using (var sink = new FileLogSink(second, LogPath)) second.Info(LogSubsystem.Io, "session two");

        var text = string.Join(Environment.NewLine, ReadLog(LogPath));
        text.Should().Contain("session one").And.Contain("session two");
    }

    [Fact]
    public void Stops_writing_once_disposed()
    {
        var log = NewLog();
        var sink = new FileLogSink(log, LogPath);
        log.Info(LogSubsystem.Io, "kept");
        sink.Dispose();

        log.Info(LogSubsystem.Io, "after dispose");

        var text = string.Join(Environment.NewLine, ReadLog(LogPath));
        text.Should().Contain("kept").And.NotContain("after dispose");
    }

    [Fact]
    public void Creates_the_directory_if_it_does_not_exist()
    {
        var nested = Path.Combine(_dir, "logs", "nested", "rimmanager.log");

        var log = NewLog();
        using var sink = new FileLogSink(log, nested);
        log.Info(LogSubsystem.Io, "made it");

        File.Exists(nested).Should().BeTrue();
    }

    /// <summary>Entries below the minimum level never reach disk either.</summary>
    [Fact]
    public void Respects_the_minimum_level()
    {
        var log = NewLog();
        log.MinimumLevel = LogLevel.Warn;
        using var sink = new FileLogSink(log, LogPath);

        log.Debug(LogSubsystem.Ui, "chatter");
        log.Error(LogSubsystem.Ui, "boom");

        var lines = ReadLog(LogPath);
        lines.Should().ContainSingle().Which.Should().Contain("boom");
    }
}
