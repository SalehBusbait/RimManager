using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Scanning;

/// <summary>
/// The counts behind 2k's first-scan line. Every one of them is a claim the window
/// makes to the user, so each is asserted against a scan rather than reasoned about.
/// </summary>
public sealed class ScanProgressTests
{
    private static InMemoryFileSystem Fs() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-08-02T00:00:00Z")));

    private static void AddMod(InMemoryFileSystem fs, string folder, string packageId) =>
        fs.AddFile($"{folder}/About/About.xml",
            $"<ModMetaData><packageId>{packageId}</packageId><name>{packageId}</name></ModMetaData>");

    /// <summary>
    /// The total is right from the FIRST report. A bar whose maximum grows while it
    /// runs travels backwards on screen, which reads as a scan that is losing ground.
    /// </summary>
    [Fact]
    public void The_total_is_final_before_the_first_folder_is_read()
    {
        var fs = Fs();
        for (var i = 0; i < 20; i++) AddMod(fs, $"/ws/{i}", $"author.mod{i}");
        AddMod(fs, "/local/mine", "author.local");

        var reports = new List<ScanProgress>();
        new ModScanner(fs).Scan(
            [new ModSourceRoot("/ws", ModSource.Workshop), new ModSourceRoot("/local", ModSource.Local)],
            progress: new SynchronousProgress(reports.Add));

        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(p => p.Total == 21, "20 workshop folders plus one local");
        reports[0].Done.Should().Be(0);
    }

    /// <summary>
    /// The last report has to reach the end, or the bar stops a few percent short and
    /// the window looks stuck at the moment it is actually finishing.
    /// </summary>
    [Fact]
    public void The_last_report_is_complete_whatever_the_folder_count()
    {
        // 13 is deliberately not a multiple of the every-8th throttle.
        var fs = Fs();
        for (var i = 0; i < 13; i++) AddMod(fs, $"/ws/{i}", $"author.mod{i}");

        var reports = new List<ScanProgress>();
        new ModScanner(fs).Scan([new ModSourceRoot("/ws", ModSource.Workshop)],
            progress: new SynchronousProgress(reports.Add));

        reports[^1].Done.Should().Be(13);
        reports[^1].Fraction.Should().Be(1);
    }

    /// <summary>A root that does not exist is not counted — it is not going to be read.</summary>
    [Fact]
    public void A_missing_root_contributes_nothing_to_the_total()
    {
        var fs = Fs();
        AddMod(fs, "/ws/1", "author.one");

        var reports = new List<ScanProgress>();
        new ModScanner(fs).Scan(
            [new ModSourceRoot("/ws", ModSource.Workshop), new ModSourceRoot("/nope", ModSource.Local)],
            progress: new SynchronousProgress(reports.Add));

        reports.Should().OnlyContain(p => p.Total == 1);
    }

    /// <summary>Nothing to scan must not divide by zero, and must not read as 100%.</summary>
    [Fact]
    public void An_empty_scan_reports_a_zero_fraction_rather_than_dividing_by_zero()
    {
        new ScanProgress(0, 0, "/ws").Fraction.Should().Be(0);
    }

    [Theory]
    [InlineData(@"D:\SteamLibrary\steamapps\workshop\content\294100", "workshop/content/294100")]
    [InlineData("/home/me/.steam/steam/steamapps/workshop/content/294100", "workshop/content/294100")]
    [InlineData("/mods", "mods")]
    [InlineData("", "")]
    public void The_root_shows_as_its_last_three_segments_forward_slashed(string root, string expected)
    {
        new ScanProgress(1, 2, root).ShortRoot().Should().Be(expected);
    }

    /// <summary>
    /// <see cref="Progress{T}"/> posts to a synchronization context; the scan runs on a
    /// pool thread where there is none, so its callbacks land on ANOTHER pool thread and
    /// a test that collected them would race. This one reports inline.
    /// </summary>
    private sealed class SynchronousProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }
}
