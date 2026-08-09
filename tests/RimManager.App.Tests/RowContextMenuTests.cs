using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The row context menu's enabling rules (<c>2i</c>-8). Nine items, each of which is a dead
/// item if offered in the wrong state — and one of them deletes folders.
/// </summary>
public sealed class RowContextMenuTests
{
    private static ModRowViewModel Row(
        string id, string name, ModSource source = ModSource.Workshop, string? workshopId = null) =>
        new(new Mod
        {
            PackageId = ModId.From(id),
            Name = name,
            Source = source,
            RootPath = "/mods/" + id,
            PublishedFileId = workshopId,
        });

    [Fact]
    public void The_header_names_a_single_mod_and_counts_a_multiple_selection()
    {
        RowContextMenu.For([Row("a.b", "Alpha")], fromActivePane: true)
            .Header.Should().Be("Alpha");

        RowContextMenu.For([Row("a.b", "Alpha"), Row("c.d", "Bravo")], fromActivePane: true)
            .Header.Should().Be("2 mods selected");
    }

    /// <summary>The pane decides which of the two mutually exclusive verbs is offered.</summary>
    [Fact]
    public void The_pane_decides_between_activate_and_deactivate()
    {
        var fromActive = RowContextMenu.For([Row("a.b", "Alpha")], fromActivePane: true);
        fromActive.CanDeactivate.Should().BeTrue();
        fromActive.CanActivate.Should().BeFalse();

        var fromInactive = RowContextMenu.For([Row("a.b", "Alpha")], fromActivePane: false);
        fromInactive.CanActivate.Should().BeTrue();
        fromInactive.CanDeactivate.Should().BeFalse();
    }

    /// <summary>
    /// Open folder reveals ONE folder, so a multi-selection has no sensible answer to
    /// which — the item is hidden rather than picking arbitrarily.
    /// </summary>
    [Fact]
    public void Open_folder_is_offered_only_for_a_single_selection()
    {
        RowContextMenu.For([Row("a.b", "Alpha")], true).CanOpenFolder.Should().BeTrue();
        RowContextMenu.For([Row("a.b", "Alpha"), Row("c.d", "Bravo")], true)
            .CanOpenFolder.Should().BeFalse();
    }

    [Fact]
    public void Open_on_workshop_needs_a_workshop_id()
    {
        RowContextMenu.For([Row("a.b", "Alpha", workshopId: "12345")], true)
            .CanOpenWorkshop.Should().BeTrue();

        RowContextMenu.For([Row("a.b", "Alpha", ModSource.Local)], true)
            .CanOpenWorkshop.Should().BeFalse("a local mod has no Workshop page");
    }

    /// <summary>
    /// The one that matters. Core and the DLC are the game's own files — offering to
    /// delete them is offering to break the install, so the item is absent entirely
    /// rather than present and refusing.
    /// </summary>
    [Theory]
    [InlineData(ModSource.Core, false)]
    [InlineData(ModSource.Dlc, false)]
    [InlineData(ModSource.Workshop, true)]
    [InlineData(ModSource.Local, true)]
    public void Only_workshop_and_local_mods_can_be_deleted_from_disk(ModSource source, bool allowed)
    {
        RowContextMenu.For([Row("a.b", "Alpha", source)], true)
            .CanDeleteFromDisk.Should().Be(allowed);
    }

    /// <summary>A mixed selection containing one game file is not deletable at all.</summary>
    [Fact]
    public void One_undeletable_row_protects_the_whole_selection()
    {
        var mixed = new List<ModRowViewModel>
        {
            Row("a.b", "Alpha"),
            Row("ludeon.rimworld", "Core", ModSource.Core),
        };

        RowContextMenu.For(mixed, true).CanDeleteFromDisk.Should().BeFalse();
    }

    [Fact]
    public void An_empty_selection_offers_nothing()
    {
        var state = RowContextMenu.For([], true);

        state.IsEmpty.Should().BeTrue();
        state.CanDeactivate.Should().BeFalse();
        state.CanDeleteFromDisk.Should().BeFalse();
        state.PackageIds.Should().BeEmpty();
    }

    /// <summary>
    /// The confirmation has to distinguish this from everything else that is destructive
    /// here: the danger zone removes RimManager's records, and this removes the user's
    /// actual mod. It also has to warn that Steam will bring a subscribed mod back.
    /// </summary>
    [Fact]
    public void The_delete_confirmation_says_it_is_the_mods_own_folder()
    {
        var text = RowContextMenu.DeleteConsequence([Row("a.b", "Alpha")]);

        text.Should().Contain("Alpha");
        text.Should().Contain("mod's own folder").And.Contain("not RimManager's record");
        text.Should().Contain("cannot be undone");
        text.Should().Contain("unsubscribe",
            "a subscribed Workshop mod reappears on the next Steam sync, and that surprises people");
    }

    [Fact]
    public void The_delete_confirmation_counts_a_multiple_selection()
    {
        RowContextMenu.DeleteConsequence([Row("a.b", "Alpha"), Row("c.d", "Bravo")])
            .Should().Contain("2 mods");
    }

    [Fact]
    public void Every_selected_package_id_is_carried_for_the_actions()
    {
        var state = RowContextMenu.For([Row("a.b", "Alpha"), Row("c.d", "Bravo")], true);

        state.PackageIds.Select(p => p.Value).Should().Equal("a.b", "c.d");
    }

    /// <summary>
    /// NF-10 · "Import mod list…" appears only on a single recognized list item — the
    /// standing re-offer after the once-per-item strip. Any source qualifies here; the
    /// Workshop-only rule governs the automatic strip, not a user's own click.
    /// </summary>
    [Fact]
    public void Import_mod_list_appears_only_on_a_single_list_item()
    {
        ModRowViewModel listItem = new(new Mod
        {
            PackageId = ModId.From("author.somelist"),
            Name = "Some List",
            Source = ModSource.Local,
            RootPath = "/mods/somelist",
            Content = ContentFlags.RwList,
        });

        RowContextMenu.For([listItem], true).CanImportRwList.Should().BeTrue();
        RowContextMenu.For([Row("a.b", "Alpha")], true).CanImportRwList.Should().BeFalse();
        RowContextMenu.For([listItem, Row("a.b", "Alpha")], true)
            .CanImportRwList.Should().BeFalse("one dialog imports one file");
    }
}
