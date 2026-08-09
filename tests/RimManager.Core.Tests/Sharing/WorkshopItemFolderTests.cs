using FluentAssertions;
using RimManager.Core.Sharing;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Sharing;

/// <summary>The Workshop-item folder shape the exporter writes (NF-10 slice 3).</summary>
public sealed class WorkshopItemFolderTests
{
    private static InMemoryFileSystem Fs() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-08-08T00:00:00Z")));

    private static RwList List(string? name = "Combat Overhaul", string? author = "someone") => new()
    {
        Name = name,
        Author = author,
        Entries =
        [
            RwEntry.Mod("a.b", "A", RwSource.Workshop),
            RwEntry.Mod("c.d", "C", RwSource.Workshop),
        ],
    };

    [Fact]
    public async Task Writes_the_about_and_the_payload_and_nothing_else()
    {
        var fs = Fs();

        var folder = await WorkshopItemFolder.WriteAsync(
            fs, "/out", List(), "{\"json\":true}", "1.6");

        folder.Should().Be(Path.Combine("/out", "combat-overhaul"));

        var about = fs.ReadAllText(Path.Combine(folder, "About", "About.xml"));
        about.Should().Contain("<name>Combat Overhaul [mod list]</name>",
            "the game's own mod list shows this name to subscribers");
        about.Should().Contain("<packageId>rimmanager.list.combatoverhaul</packageId>");
        about.Should().Contain("not a mod", "the description is the one channel that reaches "
            + "a subscriber inside the game");
        about.Should().Contain("<li>1.6</li>",
            "T7 decision 6: claiming the current version avoids the in-game amber warning");

        fs.ReadAllText(Path.Combine(folder, "combat-overhaul.rwlist"))
            .Should().Be("{\"json\":true}");
    }

    /// <summary>The written folder must be what the scanner recognises: rwlist at root,
    /// no content folders.</summary>
    [Fact]
    public async Task The_written_folder_reads_back_as_a_list_item()
    {
        var fs = Fs();

        var folder = await WorkshopItemFolder.WriteAsync(fs, "/out", List(), "{}", "1.6");

        var flags = RimManager.Core.Scanning.ContentDetector.Detect(fs, folder);
        flags.Should().Be(RimManager.Core.Domain.ContentFlags.RwList,
            "an export that the importer would not recognise is a broken round trip");
    }

    [Fact]
    public async Task An_existing_folder_gets_a_numbered_sibling_never_an_overwrite()
    {
        var fs = Fs();
        await WorkshopItemFolder.WriteAsync(fs, "/out", List(), "{}", null);

        var second = await WorkshopItemFolder.WriteAsync(fs, "/out", List(), "{}", null);

        second.Should().Be(Path.Combine("/out", "combat-overhaul-2"));
    }

    [Fact]
    public async Task Hostile_names_slug_safely_and_escape_in_the_xml()
    {
        var fs = Fs();

        var folder = await WorkshopItemFolder.WriteAsync(
            fs, "/out", List(name: "A <& B> // C"), "{}", null);

        folder.Should().Be(Path.Combine("/out", "a-b-c"));
        fs.ReadAllText(Path.Combine(folder, "About", "About.xml"))
            .Should().Contain("<name>A &lt;&amp; B&gt; // C [mod list]</name>");
    }
}
