using FluentAssertions;
using RimManager.Core.Diagnostics;
using Xunit;

namespace RimManager.Core.Tests.Diagnostics;

/// <summary>
/// <c>2f</c>: the Activity panel "is the same text as the on-disk log because its job
/// is to be pasted into a GitHub issue". The panel renders its columns separately, so
/// the only thing keeping the two identical is that they build from the same strings.
/// </summary>
public sealed class LogEntryFormatterTests
{
    private static LogEntry At(LogLevel level) =>
        new(new DateTimeOffset(2026, 7, 28, 12, 6, 11, 204, TimeSpan.Zero),
            level, LogSubsystem.Sort, "Topological sort started");

    /// <summary>
    /// The panel binds <see cref="LogEntry.LevelText"/>. If it ever stops matching the
    /// file's level column, a pasted snippet stops matching the file a maintainer asks
    /// for — which was true for one build: the panel showed "Info", the file "INFO ".
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warn)]
    [InlineData(LogLevel.Error)]
    public void The_panels_level_column_appears_verbatim_in_the_file_line(LogLevel level)
    {
        var entry = At(level);

        LogEntryFormatter.Format(entry).Should().Contain(entry.LevelText);
        entry.LevelText.Should().Be(level.ToString().ToUpperInvariant().PadRight(5));
    }

    /// <summary>Padding is what keeps the subsystem column aligned in a mono font.</summary>
    [Fact]
    public void Every_level_occupies_the_same_column_width()
    {
        Enum.GetValues<LogLevel>()
            .Select(l => LogEntryFormatter.LevelColumn(l).Length)
            .Distinct()
            .Should().ContainSingle();
    }

    [Fact]
    public void A_line_reads_time_level_subsystem_message()
    {
        LogEntryFormatter.Format(At(LogLevel.Warn))
            .Should().Be("12:06:11.204 WARN  sort Topological sort started");
    }
}
