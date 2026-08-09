using System.Collections.Immutable;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using RimManager.Core.Validation;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The Warnings tab's arrangement (<c>2a</c>). The group order is the whole point of
/// the screen and is invisible to any launch smoke — a list rendered in the wrong
/// order still looks like a working dock.
/// </summary>
public sealed class WarningsPresenterTests
{
    private static ModId Id(string value) => ModId.From(value);

    private static readonly Dictionary<ModId, string> Names = new()
    {
        [Id("a.one")] = "Rimefeller",
        [Id("b.two")] = "Rimatomics",
        [Id("c.three")] = "Dubs Bad Hygiene",
    };

    private static ValidationIssue Issue(string code, ValidationSeverity severity, string message) =>
        new(severity, code, message, Id("a.one"));

    private static SortResult SortWithCycle(RuleSource source = RuleSource.Community)
    {
        ImmutableArray<ModId> cycle = [Id("a.one"), Id("b.two"), Id("c.three")];
        var edge = new OrderingEdge(
            Id("c.three"), Id("a.one"), new RuleProvenance(source, RuleType.LoadAfter));

        return new SortResult(
            cycle,
            ImmutableDictionary<ModId, Tier>.Empty,
            [],
            [],
            [new CycleReport(cycle)],
            [new BrokenEdge(edge, cycle)]);
    }

    private static ImmutableArray<WarningEntry> AllSix()
    {
        var issues = new[]
        {
            Issue(IssueCodes.MissingDependency, ValidationSeverity.Error, "Requires 'x.y' — not installed"),
            Issue(IssueCodes.IncompatibleActive, ValidationSeverity.Error, "Incompatible with 'p.q'"),
            Issue(IssueCodes.OrderViolated, ValidationSeverity.Warning, "Should load after 'm.n'"),
            Issue(IssueCodes.UnsupportedVersion, ValidationSeverity.Warning, "Version 1.6 not declared"),
        };
        var scan = new[] { new ModWarning(WarningSeverity.Warning, "duplicate.packageId", "dup", Id("a.one")) };

        return WarningsPresenter.BuildIssues(issues, SortWithCycle(), scan, Names);
    }

    /// <summary>
    /// The six groups always render in this order, most blocking first. Cycles sits
    /// third — a category here, never its own tab (non-negotiable #7).
    /// </summary>
    [Fact]
    public void The_six_groups_always_render_in_the_designed_order()
    {
        var rows = WarningsPresenter.Group(AllSix(), tone: null, search: null);

        rows.Where(r => r.IsGroupHeader).Select(r => r.Group).Should().Equal(
            WarningGroup.MissingDependencies,
            WarningGroup.Incompatibilities,
            WarningGroup.Cycles,
            WarningGroup.LoadOrder,
            WarningGroup.UnsupportedVersion,
            WarningGroup.Duplicates);
    }

    [Fact]
    public void Every_group_header_is_immediately_followed_by_its_own_rows()
    {
        var rows = WarningsPresenter.Group(AllSix(), tone: null, search: null);

        WarningGroup? current = null;
        foreach (var row in rows)
        {
            if (row.IsGroupHeader) { current = row.Group; continue; }
            row.Group.Should().Be(current, "a row must sit under its own heading");
        }
    }

    [Fact]
    public void A_group_with_nothing_in_it_gets_no_header()
    {
        var only = WarningsPresenter.BuildIssues(
            [Issue(IssueCodes.OrderViolated, ValidationSeverity.Warning, "Should load after 'm.n'")],
            lastSort: null, scanWarnings: [], modNames: Names);

        var rows = WarningsPresenter.Group(only, tone: null, search: null);

        rows.Where(r => r.IsGroupHeader).Select(r => r.Group)
            .Should().Equal(WarningGroup.LoadOrder);
    }

    /// <summary>
    /// The chips "filter without regrouping": the headings that survive stay in the
    /// same relative places, so the shape of the list does not shift under the user.
    /// </summary>
    [Fact]
    public void A_severity_chip_narrows_the_rows_without_reordering_the_groups()
    {
        var rows = WarningsPresenter.Group(AllSix(), WarningTone.Blocking, search: null);

        rows.Where(r => r.IsGroupHeader).Select(r => r.Group).Should().Equal(
            WarningGroup.MissingDependencies,
            WarningGroup.Incompatibilities);
        rows.Where(r => !r.IsGroupHeader).Should().OnlyContain(r => r.Tone == WarningTone.Blocking);
    }

    [Fact]
    public void Search_matches_the_mod_name_as_well_as_the_issue_text()
    {
        var rows = WarningsPresenter.Group(AllSix(), tone: null, search: "rimefeller");

        // "Rimefeller +2" for the cycle row, which names the whole group of mods.
        rows.Where(r => !r.IsGroupHeader).Should().NotBeEmpty();
        rows.Where(r => !r.IsGroupHeader).Should().OnlyContain(r => r.ModName.StartsWith("Rimefeller"));
    }

    /// <summary>The packageId renders in mono, so it has to come out of the sentence.</summary>
    [Theory]
    [InlineData("Requires 'ceteam.cefastrack' — not installed", "Requires ", "ceteam.cefastrack", " — not installed")]
    [InlineData("no quotes at all", "no quotes at all", "", "")]
    [InlineData("one ' unbalanced", "one ' unbalanced", "", "")]
    public void The_packageId_is_split_out_of_the_message(
        string message, string head, string mono, string tail)
    {
        WarningsPresenter.SplitMessage(message).Should().Be((head, mono, tail));
    }

    /// <summary>
    /// The MOD column already names the subject; leading the widest column with its
    /// packageId spends it on a repeat. 2a's issue text starts with the verb.
    /// </summary>
    [Fact]
    public void A_leading_subject_id_is_dropped_from_the_issue_text()
    {
        var id = Id("Garethp.ReplaceStuffCompatibility");

        WarningsPresenter.StripSubject(
            "'Garethp.ReplaceStuffCompatibility' requires 'Replace Stuff', which is not active.", id)
            .Should().Be("Requires 'Replace Stuff', which is not active.");
    }

    [Fact]
    public void A_message_that_does_not_start_with_its_subject_is_left_alone()
    {
        WarningsPresenter.StripSubject("Requires 'x.y' — not installed", Id("a.one"))
            .Should().Be("Requires 'x.y' — not installed");

        WarningsPresenter.StripSubject("no subject at all", null)
            .Should().Be("no subject at all");
    }

    /// <summary>
    /// Non-negotiable #12: ship real icon geometries, never Unicode glyphs — several
    /// arrows are missing from default Linux fonts, and the tofu box is a visible bug.
    /// </summary>
    [Fact]
    public void No_action_label_smuggles_in_a_unicode_arrow()
    {
        var labels = AllSix()
            .Select(e => e.Fix)
            .Concat(AllSix().SelectMany(e =>
                WarningsPresenter.BuildDetail(e, SortWithCycle(), _ => null, Names)
                    .Actions.Select(a => a.Label)));

        foreach (var label in labels)
            label.Should().NotContainAny("↗", "→", "⤢", "↑", "↓");
    }

    // --- the detail panel ---------------------------------------------------

    [Fact]
    public void A_cycle_renders_every_edge_in_the_loop_and_closes_it()
    {
        var sort = SortWithCycle();
        var cycle = AllSix().Single(e => e.Group == WarningGroup.Cycles);

        var detail = WarningsPresenter.BuildDetail(cycle, sort, _ => null, Names);

        detail.Chain.Should().HaveCount(3, "a 3-mod cycle has 3 edges, including the one that closes it");
        detail.Chain[^1].After.Should().Be("Rimefeller", "the last edge must return to the first mod");
        detail.Chain.Select(s => s.Indent).Should().Equal(0, 1, 2);
    }

    /// <summary>
    /// Exactly one edge is struck. Strike none and "cycle broken" is an unsupported
    /// claim; strike more than one and the panel is lying about what the sorter did.
    /// </summary>
    [Fact]
    public void Exactly_one_edge_in_the_chain_is_marked_dropped()
    {
        var sort = SortWithCycle();
        var cycle = AllSix().Single(e => e.Group == WarningGroup.Cycles);

        var detail = WarningsPresenter.BuildDetail(cycle, sort, _ => null, Names);

        detail.Chain.Count(s => s.IsDropped).Should().Be(1);
        var dropped = detail.Chain.Single(s => s.IsDropped);
        dropped.Before.Should().Be("Dubs Bad Hygiene");
        dropped.After.Should().Be("Rimefeller");
    }

    /// <summary>
    /// CLAUDE.md's standing decision: RimSort's Community-Rules-Database ships
    /// comments, not votes, so the mockup's "3 votes" is data we do not have. The
    /// reason has to stand without it.
    /// </summary>
    [Fact]
    public void The_drop_reason_never_claims_a_vote_count()
    {
        foreach (var source in Enum.GetValues<RuleSource>())
        {
            var sort = SortWithCycle(source);
            var cycle = WarningsPresenter
                .BuildIssues([], sort, [], Names)
                .Single(e => e.Group == WarningGroup.Cycles);

            var detail = WarningsPresenter.BuildDetail(cycle, sort, _ => null, Names);

            detail.ChainNote.Should().NotBeEmpty();
            detail.ChainNote.Should().NotContainEquivalentOf("vote");
        }
    }

    /// <summary>"Not in the load order" must not render as "loads first".</summary>
    [Fact]
    public void An_inactive_affected_row_reads_as_inactive_not_as_position_zero()
    {
        var sort = SortWithCycle();
        var cycle = AllSix().Single(e => e.Group == WarningGroup.Cycles);

        var detail = WarningsPresenter.BuildDetail(
            cycle, sort, id => id == Id("a.one") ? 17 : null, Names);

        detail.Affected.Should().HaveCount(3);
        detail.Affected[0].Index.Should().Be("17");
        detail.Affected[0].Note.Should().BeEmpty();
        detail.Affected[1].Index.Should().Be("—");
        detail.Affected[1].Note.Should().Be("inactive");
    }

    [Fact]
    public void A_group_header_has_no_detail()
    {
        var header = WarningsPresenter.Group(AllSix(), null, null).First(r => r.IsGroupHeader);

        WarningsPresenter.BuildDetail(header, SortWithCycle(), _ => null, Names)
            .Should().Be(WarningDetail.None);
    }

    /// <summary>
    /// The day the old tripwire promised arrived: every action the panel offers is now
    /// ENABLED, because the markup binds RunWarningActionCommand — an action here is a
    /// contract that something will happen. The cycle offers exactly the two the hub
    /// implements: accept (pins the edge on the modlist's EdgeSuppressions) and
    /// edit-rule (the R7 editor). "Show graph" and "Drop a different edge…" are gone
    /// with the retired graph (v2 S-CYCLE).
    /// </summary>
    [Fact]
    public void Cycle_actions_are_accept_and_edit_rule_both_enabled_and_tipped()
    {
        var cycle = AllSix().Single(e => e.Group == WarningGroup.Cycles);

        var detail = WarningsPresenter.BuildDetail(cycle, SortWithCycle(), _ => null, Names);

        detail.Actions.Select(a => a.Id).Should().Equal("accept", "edit-rule");
        detail.Actions.Should().ContainSingle(a => a.IsPrimary);
        detail.Actions.Should().OnlyContain(a => a.IsEnabled && a.Tip.Length > 0);
    }

    [Fact]
    public void A_plain_warning_offers_its_one_fix_enabled_and_no_ignore()
    {
        var entry = new WarningEntry(
            WarningGroup.LoadOrder, WarningTone.Warning, IsGroupHeader: false,
            "Should load before ", "a.b", ".", "A Mod", "Order · rules", "Review",
            Subject: ModId.From("a.b"));

        var detail = WarningsPresenter.BuildDetail(entry, null, _ => null,
            new Dictionary<ModId, string>());

        detail.Actions.Should().ContainSingle().Which.Should().Match<WarningAction>(
            a => a.Id == "fix" && a.IsEnabled && a.Tip.Length > 0);
        detail.Actions.Should().NotContain(a => a.Label == "Ignore this warning",
            "there is no per-warning ignore store, and a button that has waited five "
            + "phases for one is furniture, not a promise");
    }

    /// <summary>
    /// A warning without a fix offers NO actions and hides the row button
    /// (<see cref="WarningEntry.HasFix"/>): an unsupported version has no ignore
    /// store to land in, so nothing is offered rather than a dead verb.
    /// </summary>
    [Fact]
    public void A_warning_without_a_fix_offers_no_actions_at_all()
    {
        var entry = new WarningEntry(
            WarningGroup.UnsupportedVersion, WarningTone.Warning, IsGroupHeader: false,
            "Version 1.6 not declared", "", "", "A Mod", "Unsupported", "",
            Subject: ModId.From("a.b"));

        entry.HasFix.Should().BeFalse();
        WarningsPresenter.BuildDetail(entry, null, _ => null, new Dictionary<ModId, string>())
            .Actions.Should().BeEmpty();
    }
}
