using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The hub view model is ONE class in SEVERAL partial files, split by surface (N11).
/// These pin the shape so the god-file cannot quietly regrow: the split was chosen
/// over child view models precisely because it changes nothing observable, and that
/// property only survives while the files stay small enough to reason about.
/// </summary>
public sealed class HubShapeTests
{
    private static string[] HubFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepoPaths.Root, "src", "RimManager.App", "ViewModels"),
                "MainWindowViewModel*.cs")
            .ToArray();

    [Fact]
    public void The_hub_stays_split()
    {
        HubFiles().Length.Should().BeGreaterThan(1,
            "the N11 split put MainWindowViewModel into partial files by surface; "
            + "one file means someone merged it back");
    }

    /// <summary>
    /// No partial file grows past 1,800 lines. The pre-split hub hit 7,000 — the cap
    /// is what makes "just add it to the hub" eventually force a conversation about
    /// where the member belongs.
    /// </summary>
    [Fact]
    public void No_hub_file_exceeds_the_cap()
    {
        var oversized = HubFiles()
            .Select(f => (File: Path.GetFileName(f), Lines: File.ReadAllLines(f).Length))
            .Where(x => x.Lines > 1800)
            .ToList();

        oversized.Should().BeEmpty(
            "a hub file past the cap is the god object regrowing; move a surface out "
            + "or split the file further");
    }
}
