using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// The change that makes separators survive a restart: the active pane comes from the
/// modlist, not from ModsConfig.xml — a flat list of packageIds that cannot express a
/// separator, a group or a collapsed section.
/// </summary>
public sealed class ModlistStartupTests
{
    private static Mod Installed(string id, string? name = null) => new()
    {
        PackageId = ModId.From(id),
        Name = name ?? id,
        Source = ModSource.Workshop,
        RootPath = @"C:\mods\" + id,
    };

    private static Modlist ListOf(ModlistState state, string? appliedHash = null) => new()
    {
        Id = "l1",
        Name = "Test",
        State = state,
        LastAppliedHash = appliedHash,
    };

    private static (IReadOnlyDictionary<ModId, Mod> ById, Mod[] All) Disk(params Mod[] mods) =>
        (mods.ToDictionary(m => m.PackageId), mods);

    [Fact]
    public void Separators_are_rows_and_keep_their_position()
    {
        var (byId, all) = Disk(Installed("a"), Installed("b"));
        var list = ListOf(ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Separator("s1", "Frameworks", paletteIndex: 2),
            ModlistEntry.Mod(ModId.From("a")),
            ModlistEntry.Separator("s2", "Content"),
            ModlistEntry.Mod(ModId.From("b")),
        ]));

        var plan = ModlistStartup.Resolve(list, byId, all);

        plan.Active.Should().HaveCount(4);
        plan.Active[0].Entry.DisplayName.Should().Be("Frameworks");
        plan.Active[0].Entry.PaletteIndex.Should().Be(2);
        plan.Active[2].Entry.DisplayName.Should().Be("Content");
        plan.Active.Select(r => r.Entry.Id).Should().Equal(["s1", "a", "s2", "b"],
            "the list's order IS the load order");
    }

    [Fact]
    public void Installed_mods_the_list_does_not_name_go_to_the_inactive_pane_by_name()
    {
        var (byId, all) = Disk(Installed("z", "Zebra"), Installed("a", "Apple"), Installed("m", "Mango"));
        var list = ListOf(ModlistState.Empty.WithEntries([ModlistEntry.Mod(ModId.From("m"))]));

        var plan = ModlistStartup.Resolve(list, byId, all);

        plan.Inactive.Select(m => m.Name).Should().Equal(["Apple", "Zebra"],
            "the inactive pane is a library to search, so it sorts by name");
    }

    /// <summary>
    /// A silently shortened load order is how someone discovers at 2am that the list they
    /// were sent does not work. The row stays and says which mod is absent.
    /// </summary>
    [Fact]
    public void A_mod_the_list_names_but_the_disk_lacks_stays_as_a_row_and_is_reported()
    {
        var (byId, all) = Disk(Installed("have"));
        var list = ListOf(ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Mod(ModId.From("have")),
            new ModlistEntry(ModlistEntryKind.Mod, "gone", "Some Removed Mod",
                Source: ModSource.Workshop, PublishedFileId: "2009463077"),
        ]));

        var plan = ModlistStartup.Resolve(list, byId, all);

        plan.Active.Should().HaveCount(2, "the row is kept so the gap is visible");
        plan.Active[1].IsMissing.Should().BeTrue();
        plan.Active[1].Entry.DisplayName.Should().Be("Some Removed Mod");

        plan.HasMissing.Should().BeTrue();
        plan.Missing.Should().ContainSingle()
            .Which.PublishedFileId.Should().Be("2009463077",
                "identity on the entry is what lets the app offer to fetch it back");
    }

    [Fact]
    public void A_missing_mod_is_not_also_offered_as_inactive()
    {
        var (byId, all) = Disk(Installed("have"));
        var list = ListOf(ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Mod(ModId.From("have")),
            ModlistEntry.Mod(ModId.From("gone")),
        ]));

        var plan = ModlistStartup.Resolve(list, byId, all);

        plan.Inactive.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_list_leaves_every_installed_mod_inactive()
    {
        var (byId, all) = Disk(Installed("a"), Installed("b"));

        var plan = ModlistStartup.Resolve(ListOf(ModlistState.Empty), byId, all);

        plan.Active.Should().BeEmpty("a new list starts empty, and that is a real choice");
        plan.Inactive.Should().HaveCount(2);
    }

    [Fact]
    public void A_disabled_entry_is_still_a_row_in_the_active_pane()
    {
        var (byId, all) = Disk(Installed("a"));
        var list = ListOf(ModlistState.Empty.WithEntries(
            [ModlistEntry.Mod(ModId.From("a"), enabled: false)]));

        var plan = ModlistStartup.Resolve(list, byId, all);

        plan.Active.Should().ContainSingle("membership is which pane the row is in (#2)");
        plan.Inactive.Should().BeEmpty();
    }

    // --- seeding a list from the game ---------------------------------------

    [Fact]
    public void Seeding_from_the_game_captures_identity_for_installed_mods()
    {
        var (byId, _) = Disk(Installed("a", "Alpha"));

        var state = ModlistStartup.FromGame([ModId.From("a")], byId);

        var entry = state.Entries.Single();
        entry.DisplayName.Should().Be("Alpha");
        entry.Source.Should().Be(ModSource.Workshop,
            "identity is captured at seed time so a later export survives uninstalling it");
    }

    [Fact]
    public void Seeding_keeps_a_mod_the_game_lists_but_the_disk_lacks()
    {
        var (byId, _) = Disk();

        var state = ModlistStartup.FromGame([ModId.From("ghost")], byId);

        state.AllModIds().Select(m => m.Value).Should().Equal(["ghost"],
            "ModsConfig naming an uninstalled mod is a real and common state");
    }

    [Fact]
    public void Seeding_preserves_the_games_order_exactly()
    {
        var (byId, _) = Disk(Installed("a"), Installed("b"), Installed("c"));

        var state = ModlistStartup.FromGame(
            [ModId.From("c"), ModId.From("a"), ModId.From("b")], byId);

        state.AllModIds().Select(m => m.Value).Should().Equal(["c", "a", "b"]);
    }
}
