using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

public sealed class TagResolverTests
{
    private static TagSet Tags(params Tag[] tags) => new([.. tags]);

    private static Tag T(string id, string name, int palette = 0, bool stripe = true) =>
        new() { Id = id, Name = name, PaletteIndex = palette, ShowAsStripe = stripe };

    [Fact]
    public void Resolve_preserves_assignment_order_and_skips_unknown_ids()
    {
        var set = Tags(T("a", "Alpha"), T("b", "Beta"), T("c", "Gamma"));

        var resolved = TagResolver.Resolve(set, ["c", "missing", "a"]);

        resolved.Select(t => t.Name).Should().Equal("Gamma", "Alpha");
    }

    [Fact]
    public void Resolve_empty_ids_yields_empty()
    {
        TagResolver.Resolve(Tags(T("a", "A")), ImmutableArray<string>.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Unassigned_returns_defined_tags_not_on_the_mod()
    {
        var set = Tags(T("a", "Alpha"), T("b", "Beta"), T("c", "Gamma"));

        TagResolver.Unassigned(set, ["b"]).Select(t => t.Id).Should().Equal("a", "c");
    }

    [Fact]
    public void NextPaletteIndex_cycles_through_the_six_hues()
    {
        TagResolver.NextPaletteIndex(0).Should().Be(0);
        TagResolver.NextPaletteIndex(1).Should().Be(1);
        TagResolver.NextPaletteIndex(Palette.Count).Should().Be(0, "the palette wraps");
        TagResolver.NextPaletteIndex(Palette.Count + 2).Should().Be(2);
    }

    /// <summary>Pills carry EVERY assigned tag in manage-list order (v2 §4A.1) —
    /// the one-tag stripe contest is the clause the redesign overturned.</summary>
    [Fact]
    public void PillsFor_carries_every_assigned_tag_in_manage_list_order()
    {
        var set = Tags(T("a", "Alpha"), T("b", "Beta"), T("c", "Gamma"));

        var pills = TagResolver.PillsFor(set, ["c", "b"]);

        pills.Should().HaveCount(2);
        pills[0].Name.Should().Be("Beta", "manage-list order, not assignment order");
        pills[1].Name.Should().Be("Gamma");
    }

    [Fact]
    public void PillsFor_skips_tags_with_show_on_rows_turned_off()
    {
        var set = Tags(T("a", "Alpha", stripe: false), T("b", "Beta"));

        var pills = TagResolver.PillsFor(set, ["a", "b"]);

        pills.Should().ContainSingle().Which.Name.Should().Be("Beta",
            "the per-tag flag keeps its meaning — show this tag on rows");
    }

    [Fact]
    public void PillsFor_is_empty_when_the_mod_has_no_tags()
    {
        TagResolver.PillsFor(Tags(T("a", "A")), ImmutableArray<string>.Empty).Should().BeEmpty();
    }
}
