using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The row's single status slot (1f): "strict precedence … A row never shows two."
/// One 16px slot with one answer is what lets a 200-row column be scanned
/// vertically without decoding combinations.
/// </summary>
public sealed class RowStatusTests
{
    private static ModRowViewModel Row(bool hasErrors = false) =>
        new(new Mod
        {
            PackageId = ModId.From("author.mod"),
            Name = "Mod",
            Source = ModSource.Workshop,
            RootPath = "/mod",
            // HasErrors is derived from the parse warnings, so a real error warning
            // is what puts the row in the state the scanner would.
            Warnings = hasErrors
                ? [new ModWarning(WarningSeverity.Error, "about.unreadable", "About.xml is malformed")]
                : [],
        });

    [Fact]
    public void A_clean_mod_shows_nothing()
    {
        var row = Row();

        row.Status.Should().Be(RowStatus.None);
        row.IsBroken.Should().BeFalse();
        row.IsWarning.Should().BeFalse();
        row.HasUpdate.Should().BeFalse();
        row.IsGitDirty.Should().BeFalse();
        row.StatusTip.Should().BeNull();
    }

    [Fact]
    public void A_mod_with_parse_errors_starts_at_warning() =>
        Row(hasErrors: true).Status.Should().Be(RowStatus.Warning);

    /// <summary>
    /// The ordering the design states: broken → warning → update → git dirty.
    /// </summary>
    [Fact]
    public void Precedence_runs_broken_over_warning_over_update_over_git()
    {
        ((int)RowStatus.Broken).Should().BeGreaterThan((int)RowStatus.Warning);
        ((int)RowStatus.Warning).Should().BeGreaterThan((int)RowStatus.UpdateAvailable);
        ((int)RowStatus.UpdateAvailable).Should().BeGreaterThan((int)RowStatus.GitDirty);
        ((int)RowStatus.GitDirty).Should().BeGreaterThan((int)RowStatus.None);
    }

    /// <summary>
    /// The validator, the update check and the git scan report independently and in
    /// no fixed order. RaiseStatus is what makes the outcome the same regardless.
    /// </summary>
    [Fact]
    public void Raise_keeps_the_highest_regardless_of_report_order()
    {
        var ascending = Row();
        ascending.RaiseStatus(RowStatus.GitDirty);
        ascending.RaiseStatus(RowStatus.UpdateAvailable);
        ascending.RaiseStatus(RowStatus.Broken);

        var descending = Row();
        descending.RaiseStatus(RowStatus.Broken);
        descending.RaiseStatus(RowStatus.UpdateAvailable);
        descending.RaiseStatus(RowStatus.GitDirty);

        ascending.Status.Should().Be(RowStatus.Broken);
        descending.Status.Should().Be(RowStatus.Broken);
    }

    [Fact]
    public void Only_one_status_flag_is_ever_true()
    {
        foreach (var status in Enum.GetValues<RowStatus>())
        {
            var row = Row();
            row.Status = status;

            var lit = new[] { row.IsBroken, row.IsWarning, row.HasUpdate, row.IsGitDirty }
                .Count(x => x);

            lit.Should().BeLessThanOrEqualTo(1, $"{status} must light exactly one slot");
        }
    }

    /// <summary>Colour is never the only signal — every lit state names itself.</summary>
    [Fact]
    public void Every_visible_status_carries_a_tooltip()
    {
        foreach (var status in Enum.GetValues<RowStatus>().Where(s => s != RowStatus.None))
        {
            var row = Row();
            row.Status = status;
            row.StatusTip.Should().NotBeNullOrWhiteSpace();
        }
    }

    // --- tag pills (v2 §4A.1) ------------------------------------------------

    [Fact]
    public void An_untagged_row_has_no_pills()
    {
        var row = Row();

        // Empty renders NOTHING rather than grey, so an untagged list reads as a
        // clean edge — the stripe's old rule, kept by the pills.
        row.Pills.Should().BeEmpty();
    }

    [Fact]
    public void A_tagged_row_carries_every_pill()
    {
        var row = Row();
        row.Pills = [new TagPill("UI", Palette.Violet), new TagPill("QoL", Palette.Green)];

        row.Pills.Should().HaveCount(2,
            "every assigned tag is represented — the one-tag stripe is the clause v2 overturned");
    }

    /// <summary>Core and DLC anchor the order and render at 600 (1f). Pinned was the
    /// third, and went with the vault (O13).</summary>
    [Theory]
    [InlineData(ModSource.Core, true)]
    [InlineData(ModSource.Dlc, true)]
    [InlineData(ModSource.Workshop, false)]
    [InlineData(ModSource.Local, false)]
    public void Anchor_weight_is_for_core_and_dlc(ModSource source, bool expected)
    {
        var row = new ModRowViewModel(new Mod
        {
            PackageId = ModId.From("a.b"),
            Name = "M",
            Source = source,
            RootPath = "/m",
        });

        row.IsAnchor.Should().Be(expected);
    }
}
