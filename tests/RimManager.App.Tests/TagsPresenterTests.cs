using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>Settings ▸ Tags &amp; metadata (<c>2g</c>): the counts, the condition text, and
/// the rules that stop a tag rule from doing something silently destructive.</summary>
public sealed class TagsPresenterTests
{
    private static Tag Tag(string name, params TagCondition[] conditions) => new()
    {
        Id = name.ToLowerInvariant(),
        Name = name,
        AutoAssign = [.. conditions],
    };

    [Fact]
    public void The_header_counts_tags_and_the_mods_using_them()
    {
        TagsPresenter.Header(7, 141).Should().Be("7 · used on 141 mods");
        TagsPresenter.Header(1, 1).Should().Be("1 · used on 1 mod");
    }

    /// <summary>
    /// "used on 0 mods" reads as a failure; "not used yet" reads as a next step. A fresh
    /// tag has never been applied to anything and that is normal.
    /// </summary>
    [Fact]
    public void Unused_tags_read_as_a_next_step_not_a_failure()
    {
        TagsPresenter.Header(3, 0).Should().Be("3 · not used yet");
        TagsPresenter.Header(0, 0).Should().Be("none yet");
    }

    /// <summary>An em dash, not a blank: blank reads as "we did not look".</summary>
    [Fact]
    public void An_unruled_tag_shows_a_dash()
    {
        TagsPresenter.RulesLabel(0).Should().Be("—");
        TagsPresenter.RulesLabel(2).Should().Be("2");
    }

    /// <summary>
    /// The quotes are load-bearing. A condition on <c>"Oskar "</c> matches nothing, and
    /// without the quotes the trailing space is invisible — a rule that silently never
    /// fires and no way to see why.
    /// </summary>
    [Fact]
    public void Text_conditions_are_quoted_so_stray_whitespace_is_visible()
    {
        TagsPresenter.ConditionText(new TagCondition(TagConditionKind.AuthorContains, "Oskar "))
            .Should().Be("author contains \"Oskar \"");

        TagsPresenter.ConditionText(new TagCondition(TagConditionKind.SizeOverMb, "100"))
            .Should().Be("size > 100 MB", "a number is not a string and reads worse quoted");
    }

    [Fact]
    public void Every_condition_kind_round_trips_through_its_dropdown_index()
    {
        foreach (var kind in System.Enum.GetValues<TagConditionKind>())
        {
            TagsPresenter.KindFromIndex(TagsPresenter.IndexFromKind(kind)).Should().Be(kind);
        }

        TagsPresenter.ConditionKinds.Should().HaveCount(
            System.Enum.GetValues<TagConditionKind>().Length,
            "a kind with no dropdown entry cannot be chosen");
    }

    /// <summary>
    /// The worst silent outcome on this page. An empty value matches every mod, so the
    /// rule would tag the entire library on the next scan — and auto-assign runs without
    /// asking. Unusable conditions are dropped rather than saved.
    /// </summary>
    [Fact]
    public void A_condition_that_would_match_everything_is_not_usable()
    {
        TagsPresenter.IsUsable(new TagCondition(TagConditionKind.AuthorContains, "")).Should().BeFalse();
        TagsPresenter.IsUsable(new TagCondition(TagConditionKind.AuthorContains, "   ")).Should().BeFalse();
        TagsPresenter.IsUsable(new TagCondition(TagConditionKind.AuthorContains, "Oskar")).Should().BeTrue();
    }

    /// <summary>A size rule whose value is not a number can never be evaluated.</summary>
    [Fact]
    public void A_size_condition_has_to_be_a_number()
    {
        TagsPresenter.IsUsable(new TagCondition(TagConditionKind.SizeOverMb, "big")).Should().BeFalse();
        TagsPresenter.IsUsable(new TagCondition(TagConditionKind.SizeOverMb, "100")).Should().BeTrue();
        TagsPresenter.IsUsable(new TagCondition(TagConditionKind.SizeOverMb, "12.5")).Should().BeTrue();
    }

    [Fact]
    public void New_tags_get_a_name_that_is_not_already_taken()
    {
        ImmutableArray<Tag> existing = [Tag("New tag"), Tag("New tag 2")];

        TagsPresenter.UniqueName(existing).Should().Be("New tag 3");
        TagsPresenter.UniqueName([]).Should().Be("New tag");
    }


    [Fact]
    public void The_storage_line_names_the_file_and_its_size()
    {
        TagsPresenter.StorageLine("/x/modMetadata.json", 341, 86_016)
            .Should().Be("/x/modMetadata.json · keyed by packageId · 341 entries · 84 KB");

        TagsPresenter.StorageLine("/x/modMetadata.json", 1, 10).Should().Contain("1 entry");
    }
}
