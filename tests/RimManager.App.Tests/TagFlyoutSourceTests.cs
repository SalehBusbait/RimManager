using System.IO;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// O22 · source checks over the tag assign flyout. Both decisions here are the kind
/// that a later edit would undo without anything failing — the app would build, run
/// and look fine.
/// </summary>
public class TagFlyoutSourceTests
{
    private static string RowTemplates() => File.ReadAllText(
        Path.Combine(RepoPaths.AppProject, "Views", "Mods", "ModRowTemplates.axaml"));

    [Fact]
    public void The_assign_flyout_offers_no_way_to_create_a_tag()
    {
        // Owner's call: tags are made in Settings ▸ Tags and only ASSIGNED here. The
        // create box is gone, and so are CreateTagCommand and NewTagName — putting a
        // creation control back here would re-split a decision that is now single.
        var xaml = RowTemplates();

        xaml.Should().NotContain("CreateTagCommand");
        xaml.Should().NotContain("NewTagName");
        xaml.Should().NotContain("new tag name…");
    }

    [Fact]
    public void The_flyouts_destructive_verb_is_not_the_word_Clear()
    {
        // Every visible "Clear" in this app resets a FILTER, and this flyout is a
        // deliberate mirror of the Tags ▾ filter flyout — same width, same search box,
        // same footer slot. A data deletion wearing that word, in that slot, in the
        // twin of the flyout where it means "unfilter", is a trap; and unlike a filter
        // reset it cannot be undone (metadata is not in the undo history).
        var xaml = RowTemplates();

        xaml.Should().Contain("Content=\"Remove all tags\"");
        xaml.Should().NotContain("RemoveAllTagsCommand}\" Content=\"Clear\"");
    }

    [Fact]
    public void Removing_every_tag_goes_through_the_destructive_confirm()
    {
        // Nothing brings the tags back: UndoHistory is typed on ModlistState, which
        // carries no metadata, and a Ctrl+Z would LOOK like it worked — undo reloads
        // the rows, which repaints the pills by re-reading the already-cleared file.
        var hub = RepoPaths.HubSource();

        hub.Should().Contain("private async Task RemoveAllTags()");
        hub.Should().Contain("Verb: \"Remove tags\"");
        hub.Should().Contain("if (!result.Confirmed) return;");
    }

    [Fact]
    public void Creating_a_tag_in_Settings_refreshes_the_assign_flyout()
    {
        // Since the create box went, "make one in Settings, come back and assign it" is
        // the ONLY path — and ITagStore.SaveAsync rebuilt the filters and the stripes
        // but not the assign rows, so the flyout listed the set from before the tag
        // existed. Caught by driving, not by a test, which is why this one exists.
        var hub = RepoPaths.HubSource();
        var save = hub[hub.IndexOf("Task ITagStore.SaveAsync", System.StringComparison.Ordinal)..];
        save = save[..save.IndexOf("private Task WriteTagsAsync", System.StringComparison.Ordinal)];

        save.Should().Contain("RefreshAssignRows()");
    }
}
