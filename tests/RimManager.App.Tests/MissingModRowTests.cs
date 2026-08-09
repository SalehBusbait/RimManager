using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// A mod the modlist names but the disk does not have. It is rendered rather than
/// skipped — a silently shortened load order is how someone discovers at 2am that the
/// list they were sent does not work, and skipping also made the pane quietly shorter
/// than the list with nothing saying why.
/// </summary>
public sealed class MissingModRowTests
{
    private static ModlistEntry Entry(
        string id = "some.gone.mod",
        string name = "Some Removed Mod",
        ModSource source = ModSource.Workshop,
        string? workshopId = "2009463077") =>
        new(ModlistEntryKind.Mod, id, name, Source: source,
            PublishedFileId: workshopId, ModVersion: "1.4.2");

    [Fact]
    public void A_missing_row_names_the_mod_the_list_recorded()
    {
        var row = ModRowViewModel.Missing(Entry());

        row.IsMissing.Should().BeTrue();
        row.Name.Should().Be("Some Removed Mod");
        row.PackageId.Value.Should().Be("some.gone.mod");
        row.Version.Should().Be("1.4.2");
    }

    /// <summary>
    /// No new control was invented. It reuses the Broken state, which already exists,
    /// is already styled and is already tooltipped — only the sentence differs.
    /// </summary>
    [Fact]
    public void It_reads_as_broken_but_says_why_it_is_actually_missing()
    {
        var row = ModRowViewModel.Missing(Entry());

        row.IsBroken.Should().BeTrue();
        row.StatusTip.Should().Contain("Not installed")
            .And.NotContain("About.xml", "that is the other reason a row is broken");
    }

    [Fact]
    public void An_installed_row_is_never_marked_missing()
    {
        var row = new ModRowViewModel(new Mod
        {
            PackageId = ModId.From("here"),
            Name = "Here",
            Source = ModSource.Local,
            RootPath = @"C:\mods\here",
        });

        row.IsMissing.Should().BeFalse();
        row.MissingEntry.Should().BeNull();
        row.StatusTip.Should().BeNull();
    }

    /// <summary>
    /// The identity has to survive a save. Rebuilding the entry from the packageId alone
    /// would drop the source and Workshop id, and one save would erase the only thing that
    /// makes an uninstalled mod findable again.
    /// </summary>
    [Fact]
    public void The_row_keeps_the_entry_so_a_save_cannot_erase_its_identity()
    {
        var original = Entry();

        var row = ModRowViewModel.Missing(original);

        row.MissingEntry.Should().BeSameAs(original);
        row.MissingEntry!.PublishedFileId.Should().Be("2009463077");
        row.MissingEntry.Source.Should().Be(ModSource.Workshop);
    }

    [Fact]
    public void A_missing_local_mod_does_not_get_relabelled_as_workshop()
    {
        var row = ModRowViewModel.Missing(
            Entry(source: ModSource.Local, workshopId: null));

        row.MissingEntry!.Source.Should().Be(ModSource.Local);

        // The badge is an icon since N1, so what the row must get right is WHICH icon
        // and tint the styles select — and the tooltip, now the only place the source
        // is written out.
        row.IsLocalSource.Should().BeTrue();
        row.IsWorkshopSource.Should().BeFalse();
        row.Source.Should().StartWith("Local");
    }

    /// <summary>An entry recorded before identity existed still has to render.</summary>
    [Fact]
    public void An_entry_with_no_recorded_source_still_produces_a_row()
    {
        var row = ModRowViewModel.Missing(
            new ModlistEntry(ModlistEntryKind.Mod, "old.entry", "Old Entry"));

        row.IsMissing.Should().BeTrue();
        row.Name.Should().Be("Old Entry");
    }
}
