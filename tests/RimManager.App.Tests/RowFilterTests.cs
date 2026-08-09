using System.Collections.Immutable;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

public sealed class RowFilterTests
{
    private static Mod Mod(
        string id, string name, ModSource source = ModSource.Workshop,
        ContentFlags content = ContentFlags.None, string[]? versions = null, bool warning = false)
        => new()
        {
            PackageId = ModId.From(id),
            Name = name,
            Source = source,
            RootPath = "/" + id,
            Content = content,
            SupportedVersions = versions is null ? [] : [.. versions],
            Warnings = warning ? [new ModWarning(WarningSeverity.Error, "x", "y")] : [],
        };

    [Fact]
    public void Empty_criteria_match_everything()
    {
        RowFilter.Matches(Mod("a.b", "Anything"), new FilterCriteria()).Should().BeTrue();
    }

    [Fact]
    public void Plain_search_matches_name_and_package_id_case_insensitively()
    {
        var mod = Mod("brrainz.harmony", "Harmony");
        RowFilter.Matches(mod, new FilterCriteria { Search = "harm" }).Should().BeTrue();
        RowFilter.Matches(mod, new FilterCriteria { Search = "BRRAINZ" }).Should().BeTrue();
        RowFilter.Matches(mod, new FilterCriteria { Search = "nomatch" }).Should().BeFalse();
    }

    [Fact]
    public void Regex_search_applies_and_invalid_pattern_does_not_hide_everything()
    {
        var mod = Mod("a.b", "Vanilla Expanded");
        RowFilter.Matches(mod, new FilterCriteria { Search = "^Vanilla", UseRegex = true }).Should().BeTrue();
        RowFilter.Matches(mod, new FilterCriteria { Search = "^Zzz", UseRegex = true }).Should().BeFalse();
        // An incomplete pattern must not hide the row while the user is still typing.
        RowFilter.Matches(mod, new FilterCriteria { Search = "(unclosed", UseRegex = true }).Should().BeTrue();
    }

    // "C# only" went with the toolbar chip in N1 (UI-7). It filtered on the signal the
    // CDPTSL column carried, and §0a moved that signal into mod info as words — a
    // filter you cannot verify against the list it filters. FilterCriteria no longer
    // carries the option, so there is no predicate left to test.

    [Fact]
    public void Warnings_only_matches_mods_in_the_warned_set()
    {
        var warned = new[] { ModId.From("a") }.ToImmutableHashSet();
        var c = new FilterCriteria { WarningsOnly = true, WarnedIds = warned };

        RowFilter.Matches(Mod("a", "A"), c).Should().BeTrue();
        RowFilter.Matches(Mod("b", "B"), c).Should().BeFalse();
    }

    // The "Unsupported" chip went in N2 (UI-7), and the check went with it — into
    // ModListValidator.CheckVersions, which now runs over the inactive pane too, so an
    // unsupported mod carries a warning on its row instead of hiding behind a filter.

    [Fact]
    public void Predicates_and_together()
    {
        var mod = Mod("a", "Harmony Lib", content: ContentFlags.Assemblies);
        RowFilter.Matches(mod, new FilterCriteria { Search = "harmony" }).Should().BeTrue();
        RowFilter.Matches(mod, new FilterCriteria { Search = "harmony", WarningsOnly = true }).Should().BeFalse();
    }

    // --- the tag filter (N4g) ----------------------------------------------
    // Until N4g, FilterCriteria had no tag field and none of these predicates existed:
    // every checkbox in the Tags ▾ flyout hid no rows at all.

    /// <summary>a carries qol+ui, b carries qol, c carries nothing.</summary>
    private static ImmutableDictionary<ModId, ImmutableHashSet<string>> Tags() =>
        new Dictionary<ModId, ImmutableHashSet<string>>
        {
            [ModId.From("a")] = ["qol", "ui"],
            [ModId.From("b")] = ["qol"],
        }.ToImmutableDictionary();

    [Fact]
    public void Match_any_hides_every_row_carrying_none_of_the_ticked_tags()
    {
        var c = new FilterCriteria { SelectedTagIds = ["qol", "ui"], TagsByMod = Tags() };

        RowFilter.Matches(Mod("a", "A"), c).Should().BeTrue();
        RowFilter.Matches(Mod("b", "B"), c).Should().BeTrue();
        RowFilter.Matches(Mod("c", "C"), c).Should().BeFalse();
    }

    [Fact]
    public void Match_all_narrows_to_rows_carrying_every_ticked_tag()
    {
        var c = new FilterCriteria { SelectedTagIds = ["qol", "ui"], MatchAllTags = true, TagsByMod = Tags() };

        RowFilter.Matches(Mod("a", "A"), c).Should().BeTrue();
        RowFilter.Matches(Mod("b", "B"), c).Should().BeFalse();
        RowFilter.Matches(Mod("c", "C"), c).Should().BeFalse();
    }

    [Fact]
    public void Untagged_shows_only_mods_carrying_no_tags_at_all()
    {
        var c = new FilterCriteria { UntaggedOnly = true, TagsByMod = Tags() };

        // c has no metadata entry at all; that is the common case, not an edge.
        RowFilter.Matches(Mod("c", "C"), c).Should().BeTrue();
        RowFilter.Matches(Mod("a", "A"), c).Should().BeFalse();
    }

    [Fact]
    public void Untagged_is_a_pseudo_tag_and_composes_under_the_same_any_all_rule()
    {
        // Match any: carries the tag OR carries nothing — a union.
        var any = new FilterCriteria { SelectedTagIds = ["ui"], UntaggedOnly = true, TagsByMod = Tags() };
        RowFilter.Matches(Mod("a", "A"), any).Should().BeTrue("carries ui");
        RowFilter.Matches(Mod("c", "C"), any).Should().BeTrue("carries nothing");
        RowFilter.Matches(Mod("b", "B"), any).Should().BeFalse("tagged, but not ui");

        // Match all: no mod both carries a tag and carries nothing, so the honest
        // answer is the empty set — the empty state names the filters that did it.
        var all = any with { MatchAllTags = true };
        RowFilter.Matches(Mod("a", "A"), all).Should().BeFalse();
        RowFilter.Matches(Mod("c", "C"), all).Should().BeFalse();

        // Match all + Untagged alone is still just "untagged".
        var untaggedAll = new FilterCriteria { UntaggedOnly = true, MatchAllTags = true, TagsByMod = Tags() };
        RowFilter.Matches(Mod("c", "C"), untaggedAll).Should().BeTrue();
        RowFilter.Matches(Mod("a", "A"), untaggedAll).Should().BeFalse();
    }

    [Fact]
    public void Tag_filter_ands_with_the_other_predicates()
    {
        var c = new FilterCriteria { SelectedTagIds = ["qol"], TagsByMod = Tags(), Search = "Alpha" };

        RowFilter.Matches(Mod("a", "Alpha"), c).Should().BeTrue();
        RowFilter.Matches(Mod("b", "Beta"), c).Should().BeFalse("carries qol but fails the search");
        RowFilter.Matches(Mod("c", "Alpha Two"), c).Should().BeFalse("matches the search but carries no tag");
    }

    [Fact]
    public void Clearing_the_tag_filter_makes_the_criteria_empty_again()
    {
        new FilterCriteria { SelectedTagIds = ["qol"] }.IsEmpty.Should().BeFalse();
        new FilterCriteria { UntaggedOnly = true }.IsEmpty.Should().BeFalse();
        // MatchAllTags alone is a mode, not a filter.
        new FilterCriteria { MatchAllTags = true }.IsEmpty.Should().BeTrue();
    }
}
