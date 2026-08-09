using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using Xunit;

namespace RimManager.Core.Tests.Sorting;

/// <summary>
/// The "Alphabetical within separators" mode (<c>2g</c>, <c>3f</c>): ignores rules
/// entirely and sorts the contents of each hand-made group, for library-style lists.
/// Separators never move — preserving a hand-built grouping is the whole point.
/// </summary>
public sealed class AlphabeticalSorterTests
{
    private static ModlistEntry Sep(string name) => ModlistEntry.Separator("sep-" + name, name);
    private static ModlistEntry ModOf(string id) => ModlistEntry.Mod(ModId.From(id));

    private static ModlistState StateOf(params ModlistEntry[] entries) =>
        ModlistState.Empty.WithEntries(entries);

    private static List<string> Names(ModlistState state) =>
        state.Entries.Select(e => e.Kind == ModlistEntryKind.Separator
            ? "[" + e.DisplayName + "]"
            : e.Id).ToList();

    [Fact]
    public void Sorts_within_a_group_without_moving_the_separator()
    {
        var state = StateOf(
            Sep("Frameworks"),
            ModOf("z.mod"), ModOf("a.mod"), ModOf("m.mod"));

        var sorted = AlphabeticalSorter.SortWithinSeparators(state);

        Names(sorted).Should().Equal("[Frameworks]", "a.mod", "m.mod", "z.mod");
    }

    [Fact]
    public void Each_group_sorts_independently_and_groups_keep_their_order()
    {
        var state = StateOf(
            Sep("B-group"), ModOf("z.one"), ModOf("a.two"),
            Sep("A-group"), ModOf("y.three"), ModOf("b.four"));

        var sorted = AlphabeticalSorter.SortWithinSeparators(state);

        // B-group is still first: separators do not move, only their contents.
        Names(sorted).Should().Equal(
            "[B-group]", "a.two", "z.one",
            "[A-group]", "b.four", "y.three");
    }

    [Fact]
    public void Mods_above_the_first_separator_form_their_own_group()
    {
        var state = StateOf(
            ModOf("z.top"), ModOf("a.top"),
            Sep("Rest"), ModOf("m.rest"));

        Names(AlphabeticalSorter.SortWithinSeparators(state))
            .Should().Equal("a.top", "z.top", "[Rest]", "m.rest");
    }

    [Fact]
    public void A_list_with_no_separators_sorts_as_one_group()
    {
        var state = StateOf(ModOf("c.x"), ModOf("a.x"), ModOf("b.x"));

        Names(AlphabeticalSorter.SortWithinSeparators(state))
            .Should().Equal("a.x", "b.x", "c.x");
    }

    [Fact]
    public void An_empty_group_survives()
    {
        var state = StateOf(Sep("Empty"), Sep("Also empty"), ModOf("a.x"));

        Names(AlphabeticalSorter.SortWithinSeparators(state))
            .Should().Equal("[Empty]", "[Also empty]", "a.x");
    }

    [Fact]
    public void Empty_state_round_trips() =>
        AlphabeticalSorter.SortWithinSeparators(ModlistState.Empty).Entries.Should().BeEmpty();

    /// <summary>Sorting is on the DISPLAY name, not the packageId — the list shows
    /// names, so sorting by anything else would look arbitrary.</summary>
    [Fact]
    public void Sorts_by_display_name_rather_than_package_id()
    {
        var state = StateOf(ModOf("zzz.aaa"), ModOf("aaa.zzz"));
        var names = new Dictionary<ModId, string>
        {
            [ModId.From("zzz.aaa")] = "Alpha",
            [ModId.From("aaa.zzz")] = "Zulu",
        };

        Names(AlphabeticalSorter.SortWithinSeparators(state, names))
            .Should().Equal("zzz.aaa", "aaa.zzz");
    }

    [Fact]
    public void Name_comparison_ignores_case()
    {
        var state = StateOf(ModOf("a"), ModOf("b"), ModOf("c"));
        var names = new Dictionary<ModId, string>
        {
            [ModId.From("a")] = "banana",
            [ModId.From("b")] = "Apple",
            [ModId.From("c")] = "cherry",
        };

        Names(AlphabeticalSorter.SortWithinSeparators(state, names))
            .Should().Equal("b", "a", "c");
    }

    [Fact]
    public void A_mod_missing_from_the_name_lookup_falls_back_to_its_entry_name()
    {
        // zzz.aaa resolves to "Alpha" through the lookup; m.beta has no entry, so its
        // own DisplayName ("m.beta") is used. An unscanned mod still sorts somewhere
        // sensible rather than being dropped.
        var state = StateOf(ModOf("m.beta"), ModOf("zzz.aaa"));
        var names = new Dictionary<ModId, string> { [ModId.From("zzz.aaa")] = "Alpha" };

        Names(AlphabeticalSorter.SortWithinSeparators(state, names))
            .Should().Equal("zzz.aaa", "m.beta");
    }

    /// <summary>
    /// Constraint #6 applies to both algorithms: sort(sort(x)) == sort(x). The
    /// packageId tie-break is what guarantees it — two mods sharing a display name
    /// would otherwise swap places on every run.
    /// </summary>
    [Fact]
    public void Is_idempotent_even_when_display_names_collide()
    {
        var state = StateOf(
            Sep("G"), ModOf("b.dup"), ModOf("a.dup"), ModOf("c.other"));
        var names = new Dictionary<ModId, string>
        {
            [ModId.From("b.dup")] = "Same Name",
            [ModId.From("a.dup")] = "Same Name",
            [ModId.From("c.other")] = "Other",
        };

        var once = AlphabeticalSorter.SortWithinSeparators(state, names);
        var twice = AlphabeticalSorter.SortWithinSeparators(once, names);

        Names(twice).Should().Equal(Names(once));
        Names(once).Should().Equal("[G]", "c.other", "a.dup", "b.dup");
    }

    [Fact]
    public void Preserves_enabled_and_collapsed_flags()
    {
        var state = ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Separator("s", "G", Palette.Red, collapsed: true),
            ModlistEntry.Mod(ModId.From("z.mod"), enabled: false),
            ModlistEntry.Mod(ModId.From("a.mod")),
        ]);

        var sorted = AlphabeticalSorter.SortWithinSeparators(state);

        sorted.Entries[0].Collapsed.Should().BeTrue();
        sorted.Entries[0].PaletteIndex.Should().Be(Palette.Red);
        sorted.Entries.Single(e => e.Id == "z.mod").Enabled.Should().BeFalse();
    }

    [Fact]
    public void No_mod_is_lost_or_duplicated()
    {
        var state = StateOf(
            Sep("A"), ModOf("m1"), ModOf("m2"),
            Sep("B"), ModOf("m3"), ModOf("m4"), ModOf("m5"));

        var sorted = AlphabeticalSorter.SortWithinSeparators(state);

        sorted.AllModIds().Select(m => m.Value).Should().BeEquivalentTo(
            ["m1", "m2", "m3", "m4", "m5"]);
        sorted.Entries.Count.Should().Be(state.Entries.Count);
    }
}
