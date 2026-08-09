using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sharing;
using RimManager.Core.Sorting;
using Xunit;

namespace RimManager.Core.Tests.Sharing;

/// <summary>
/// Exporting a modlist. The point of this overload is the case the profile-based one gets
/// wrong: a list naming a mod that is no longer installed.
/// </summary>
public sealed class RwListBuilderModlistTests
{
    private static Mod Installed(string id, string? workshopId = null) => new()
    {
        PackageId = ModId.From(id),
        Name = id + " (installed)",
        Source = ModSource.Workshop,
        RootPath = @"C:\mods\" + id,
        PublishedFileId = workshopId,
        ModVersion = "2.0",
    };

    private static RwList Build(
        ModlistState state,
        IEnumerable<Mod>? installed = null,
        EdgeSuppressions? suppressions = null,
        IReadOnlyDictionary<ModId, DateTimeOffset>? updated = null) =>
        RwListBuilder.Build(
            state,
            (installed ?? []).ToDictionary(m => m.PackageId),
            ImmutableDictionary<ModId, ModMetadata>.Empty,
            [],
            [],
            new RwListInfo(Name: "Shared"),
            suppressions,
            updated);

    /// <summary>
    /// The bug. Reading identity off the scan meant an uninstalled mod exported as
    /// Workshop-with-no-id: silently mislabelled, and uninstallable by the recipient.
    /// </summary>
    [Fact]
    public void An_uninstalled_mod_keeps_the_identity_the_entry_recorded()
    {
        var state = ModlistState.Empty.WithEntries(
        [
            new ModlistEntry(
                ModlistEntryKind.Mod, "gone.local", "A Local Mod",
                Source: ModSource.Local, ModVersion: "1.3"),
            new ModlistEntry(
                ModlistEntryKind.Mod, "gone.workshop", "A Workshop Mod",
                Source: ModSource.Workshop, PublishedFileId: "2009463077"),
        ]);

        var list = Build(state);

        var local = list.Mods.First(m => m.PackageId == "gone.local");
        local.Source.Should().Be(RwSource.Local, "it was never a Workshop mod");
        local.DisplayName.Should().Be("A Local Mod");
        local.ModVersion.Should().Be("1.3");

        var workshop = list.Mods.First(m => m.PackageId == "gone.workshop");
        workshop.PublishedFileId.Should().Be("2009463077",
            "without this the recipient cannot install it");
    }

    [Fact]
    public void The_installed_copy_wins_where_it_exists_because_it_is_current()
    {
        var state = ModlistState.Empty.WithEntries(
        [
            new ModlistEntry(
                ModlistEntryKind.Mod, "a", "Stale name recorded long ago",
                Source: ModSource.Workshop, ModVersion: "1.0"),
        ]);

        var list = Build(state, [Installed("a", "12345")]);

        var entry = list.Mods.Single();
        entry.DisplayName.Should().Be("a (installed)");
        entry.ModVersion.Should().Be("2.0");
        entry.PublishedFileId.Should().Be("12345");
    }

    [Fact]
    public void Separators_survive_with_their_palette_and_collapse_state()
    {
        var state = ModlistState.Empty.WithEntries(
            [ModlistEntry.Separator("s1", "Frameworks", paletteIndex: 4, collapsed: true)]);

        var entry = Build(state).Entries.Single();

        entry.Type.Should().Be(RwEntryKind.Separator);
        entry.Name.Should().Be("Frameworks");
        entry.PaletteIndex.Should().Be(4);
        entry.Collapsed.Should().BeTrue();
        entry.Color.Should().NotBeNull("the hex is advisory, for tools that cannot read an index");
    }

    [Fact]
    public void Dropped_edges_are_carried_so_a_later_re_sort_does_not_relitigate_them()
    {
        var suppressions = EdgeSuppressions.Empty
            .With(ModId.From("a"), ModId.From("b"), "cycle");

        var list = Build(ModlistState.Empty, suppressions: suppressions);

        list.DroppedEdges.Should().ContainSingle();
        list.DroppedEdges[0].Before.Should().Be("a");
        list.DroppedEdges[0].After.Should().Be("b");
    }

    [Fact]
    public void No_suppressions_means_an_empty_array_not_a_null()
    {
        Build(ModlistState.Empty).DroppedEdges.Should().BeEmpty();
    }

    /// <summary>
    /// Steam publishes an update TIME and never a version, so this is the only way a
    /// recipient can tell whether a mod moved since the list was proven to work.
    /// </summary>
    [Fact]
    public void Workshop_update_times_are_stamped_when_they_are_known()
    {
        var when = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var state = ModlistState.Empty.WithEntries([ModlistEntry.Mod(ModId.From("a"))]);

        var stamped = Build(state, [Installed("a")], updated: new Dictionary<ModId, DateTimeOffset>
        {
            [ModId.From("a")] = when,
        });
        stamped.Mods.Single().TimeUpdatedUtc.Should().Be(when);

        Build(state, [Installed("a")]).Mods.Single().TimeUpdatedUtc
            .Should().BeNull("an update check needs the network and may never have run");
    }
}
