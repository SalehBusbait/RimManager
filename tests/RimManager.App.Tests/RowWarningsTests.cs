using System;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The row's warning tooltip (N2 · UI-4). It said <c>"Has warnings"</c> — which is what
/// the coloured glyph beside it already said — while the dock held the actual sentences
/// the whole time.
/// </summary>
public sealed class RowWarningsTests
{
    /// <summary>A warning line at the ordinary Warning tone.</summary>
    private static RowWarning W(string message) => new(message, WarningTone.Warning);

    private static string[] Lines(string tip) =>
        tip.Split(Environment.NewLine, StringSplitOptions.None);

    [Fact]
    public void Nothing_to_report_produces_no_tooltip_at_all()
    {
        RowWarnings.Tip([]).Should().BeEmpty();
    }

    /// <summary>
    /// One warning is written as itself. A "1 warning:" header over a single bulleted
    /// line is a heading for nothing, and it pushes the sentence that matters down.
    /// </summary>
    [Fact]
    public void A_single_warning_is_the_sentence_with_no_count_and_no_bullet()
    {
        var tip = RowWarnings.Tip([W("Requires Replace Stuff, which is not active")]);

        tip.Should().Be("Requires Replace Stuff, which is not active");
        tip.Should().NotContain("•");
        tip.Should().NotContain("1 warning");
    }

    [Fact]
    public void Several_warnings_lead_with_the_count_then_list_them()
    {
        var tip = RowWarnings.Tip([W("first thing"), W("second thing"), W("third thing")]);
        var lines = Lines(tip);

        lines[0].Should().Be("3 warnings:");
        lines.Should().HaveCount(4);
        lines.Skip(1).Should().OnlyContain(l => l.StartsWith("• "));
    }

    /// <summary>
    /// A tooltip that runs past the window edge is dismissed unread, so the list stops
    /// at four — but the remainder is COUNTED, never dropped. "and 9 more" is a
    /// different claim from silence, and silence is the one that misleads.
    /// </summary>
    [Fact]
    public void A_long_list_is_capped_and_says_how_many_it_did_not_show()
    {
        var many = Enumerable.Range(1, 13).Select(i => W($"issue {i}")).ToArray();

        var lines = Lines(RowWarnings.Tip(many));

        lines[0].Should().Be("13 warnings:");
        lines.Should().HaveCount(1 + RowWarnings.MaxListed + 1);
        lines[^1].Should().Be($"• and {13 - RowWarnings.MaxListed} more");
    }

    /// <summary>Exactly at the cap there is no remainder line to add.</summary>
    [Fact]
    public void Exactly_the_cap_adds_no_remainder_line()
    {
        var lines = Lines(RowWarnings.Tip(
            [.. Enumerable.Range(1, RowWarnings.MaxListed).Select(i => W($"issue {i}"))]));

        lines.Should().HaveCount(1 + RowWarnings.MaxListed);
        lines.Should().NotContain(l => l.Contains("more"));
    }

    [Theory]
    [InlineData(1, "1 WARNING")]
    [InlineData(2, "2 WARNINGS")]
    [InlineData(14, "14 WARNINGS")]
    public void The_section_heading_is_singular_at_one(int count, string expected)
    {
        RowWarnings.SectionHeading(count).Should().Be(expected);
    }

    /// <summary>
    /// Validator messages are sentences and end in a full stop; a bulleted list of
    /// them reads as a paragraph chopped up. Anything not ending in one is untouched.
    /// </summary>
    [Fact]
    public void List_items_lose_a_trailing_full_stop_and_nothing_else()
    {
        RowWarnings.ForList([W("Requires 'x', which is not active."), W("no stop here"), W("")])
            .Select(w => w.Message)
            .Should().Equal("Requires 'x', which is not active", "no stop here", "");
    }

    /// <summary>
    /// The row's tooltip has to change when its warnings do. A test that read
    /// <c>StatusTip</c> after setting <c>Warnings</c> would pass even with no
    /// notification at all, and the pane would render the previous pass forever.
    /// </summary>
    [Fact]
    public void Setting_the_warnings_announces_the_tooltip_and_the_heading()
    {
        var row = new ModRowViewModel(new Mod
        {
            PackageId = ModId.From("a.b"),
            Name = "A",
            Source = ModSource.Workshop,
            RootPath = "/mods/a",
        })
        {
            Status = RowStatus.Warning,
        };

        var announced = new System.Collections.Generic.List<string?>();
        row.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        row.Warnings = [W("something is wrong")];

        announced.Should().Contain(nameof(ModRowViewModel.StatusTip));
        announced.Should().Contain(nameof(ModRowViewModel.HasWarnings));
        announced.Should().Contain(nameof(ModRowViewModel.WarningHeading));
        row.StatusTip.Should().Be("something is wrong");
    }

    /// <summary>
    /// A row can be Broken with no issue naming it — an unreadable About.xml is
    /// recorded on the mod, never in the validation report — so the old generic line
    /// still has to be there for that case rather than leaving the glyph silent.
    /// </summary>
    [Fact]
    public void A_broken_row_with_no_named_warning_keeps_a_generic_tooltip()
    {
        var row = new ModRowViewModel(new Mod
        {
            PackageId = ModId.From("a.b"),
            Name = "A",
            Source = ModSource.Workshop,
            RootPath = "/mods/a",
        })
        {
            Status = RowStatus.Broken,
        };

        row.HasWarnings.Should().BeFalse();
        row.StatusTip.Should().NotBeNullOrEmpty();
        row.StatusTip.Should().Contain("About.xml");
    }

    /// <summary>
    /// The missing-mod line outranks everything: a row for a mod that is not on disk
    /// has to say THAT before it says anything about load order.
    /// </summary>
    [Fact]
    public void A_missing_mod_says_so_before_anything_else()
    {
        var row = ModRowViewModel.Missing(
            new ModlistEntry(ModlistEntryKind.Mod, "gone.mod", "Gone", Source: ModSource.Workshop));

        row.Warnings = [W("Requires something else")];

        row.StatusTip.Should().StartWith("Not installed");
    }
}
