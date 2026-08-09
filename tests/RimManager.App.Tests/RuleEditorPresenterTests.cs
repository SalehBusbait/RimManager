using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The rule editor's rows (<c>2i</c>-5). The collapsing is the point: one relationship can
/// be declared in About.xml, restated by the community DB and overridden by the user, and
/// the editor must show <b>one row with the source that governs</b> — three rows would look
/// like three rules that might disagree.
/// </summary>
public sealed class RuleEditorPresenterTests
{
    private static ModId Id(string s) => ModId.From(s);

    private static Mod ModOf(string id, string name) => new()
    {
        PackageId = Id(id),
        Name = name,
        Source = ModSource.Workshop,
        RootPath = "/m/" + id,
    };

    private static readonly Dictionary<ModId, Mod> Installed = new()
    {
        [Id("a.mod")] = ModOf("a.mod", "Alpha"),
        [Id("b.mod")] = ModOf("b.mod", "Bravo"),
        [Id("c.mod")] = ModOf("c.mod", "Charlie"),
    };

    private static LoadOrderRules Rules(params (string Subject, string[] After, string[] Before)[] entries) =>
        new(entries.ToImmutableDictionary(
            e => Id(e.Subject),
            e => new ModRules
            {
                LoadAfter = [.. e.After.Select(a => new RuleRef(Id(a)))],
                LoadBefore = [.. e.Before.Select(b => new RuleRef(Id(b)))],
            }));

    [Fact]
    public void A_load_after_rule_reads_from_the_selected_mods_point_of_view()
    {
        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("a.mod", ["b.mod"], [])), RuleOverrides.Empty, Installed);

        rows.Should().ContainSingle();
        rows[0].Other.Should().Be(Id("b.mod"));
        rows[0].OtherName.Should().Be("Bravo");
        rows[0].Direction.Should().Be(RuleDirection.After);
        rows[0].DirectionText.Should().Be("loads after");
    }

    /// <summary>
    /// The other side of the relationship is a rule about this mod too. Someone opening
    /// the editor to understand why a mod moved needs to see the rule that moved it, even
    /// when another mod is the one that declared it.
    /// </summary>
    [Fact]
    public void A_rule_declared_by_another_mod_still_appears_here()
    {
        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("b.mod", ["a.mod"], [])), RuleOverrides.Empty, Installed);

        rows.Should().ContainSingle();
        rows[0].Other.Should().Be(Id("b.mod"));
        rows[0].Direction.Should().Be(RuleDirection.Before,
            "Bravo loading after Alpha means Alpha loads before Bravo");
    }

    /// <summary>The reason this class exists.</summary>
    [Fact]
    public void A_user_rule_replaces_the_community_row_for_the_same_pair()
    {
        var overrides = RuleOverrides.Empty
            .WithUserRule(new UserRule(Id("b.mod"), Id("a.mod"), "mine"));

        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("a.mod", ["b.mod"], [])), overrides, Installed);

        rows.Should().ContainSingle("one relationship is one row, whatever declared it");
        rows[0].Source.Should().Be(RuleSource.User);
        rows[0].SourceText.Should().Be("yours");
        rows[0].IsUserRule.Should().BeTrue();
    }

    /// <summary>
    /// About.xml is somebody else's file. Editing it would be overwritten by the next
    /// Workshop update, so the row is locked rather than hidden — it is still part of why
    /// the order is what it is.
    /// </summary>
    [Fact]
    public void An_about_xml_rule_is_locked_and_cannot_be_disabled_or_deleted()
    {
        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("a.mod", ["b.mod"], [])), RuleOverrides.Empty, Installed,
            sourceOfMerged: RuleSource.About);

        rows[0].IsLocked.Should().BeTrue();
        rows[0].CanDisable.Should().BeFalse();
        rows[0].CanDelete.Should().BeFalse();
        rows[0].SourceText.Should().Be("About.xml");
    }

    [Fact]
    public void A_community_rule_can_be_disabled_but_not_deleted()
    {
        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("a.mod", ["b.mod"], [])), RuleOverrides.Empty, Installed);

        rows[0].CanDisable.Should().BeTrue();
        rows[0].CanDelete.Should().BeFalse("a community rule is switched off, never removed");
    }

    [Fact]
    public void A_user_rule_can_be_deleted_but_not_disabled()
    {
        var overrides = RuleOverrides.Empty.WithUserRule(new UserRule(Id("b.mod"), Id("a.mod")));

        var rows = RuleEditorPresenter.RowsFor(Id("a.mod"), LoadOrderRules.Empty, overrides, Installed);

        rows[0].CanDelete.Should().BeTrue();
        rows[0].CanDisable.Should().BeFalse("there is no upstream to preserve");
    }

    /// <summary>
    /// A disabled rule keeps its row. That is the whole design of <see cref="DisabledRule"/>:
    /// removing it would let the next database resync silently bring it back.
    /// </summary>
    [Fact]
    public void A_disabled_community_rule_is_still_shown_marked_as_off()
    {
        var overrides = RuleOverrides.Empty.Disable(Id("b.mod"), Id("a.mod"));

        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("a.mod", ["b.mod"], [])), overrides, Installed);

        rows.Should().ContainSingle();
        rows[0].IsDisabled.Should().BeTrue();
    }

    /// <summary>Yours first — they are what you came here to change.</summary>
    [Fact]
    public void User_rules_sort_above_community_ones()
    {
        var overrides = RuleOverrides.Empty.WithUserRule(new UserRule(Id("c.mod"), Id("a.mod")));

        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("a.mod", ["b.mod"], [])), overrides, Installed);

        rows.Should().HaveCount(2);
        rows[0].IsUserRule.Should().BeTrue();
    }

    [Fact]
    public void A_mod_with_no_rules_has_no_rows()
    {
        RuleEditorPresenter.RowsFor(Id("a.mod"), LoadOrderRules.Empty, RuleOverrides.Empty, Installed)
            .Should().BeEmpty();
    }

    /// <summary>
    /// A mod showing "4" when three are switched off would explain nothing about the order
    /// it ends up in, so the disabled ones are counted apart.
    /// </summary>
    [Fact]
    public void The_count_label_separates_active_rules_from_disabled_ones()
    {
        RuleEditorPresenter.CountLabel(4, 0).Should().Be("4");
        RuleEditorPresenter.CountLabel(1, 3).Should().Be("1 · 3 off");
    }

    /// <summary>An uninstalled mod still has an identity worth showing.</summary>
    [Fact]
    public void A_rule_pointing_at_an_uninstalled_mod_falls_back_to_its_packageId()
    {
        var rows = RuleEditorPresenter.RowsFor(
            Id("a.mod"), Rules(("a.mod", ["gone.mod"], [])), RuleOverrides.Empty, Installed);

        rows[0].OtherName.Should().Be(Id("gone.mod").Display);
    }

    [Fact]
    public void The_precedence_note_states_the_order_and_the_never_deleted_promise()
    {
        RuleEditorPresenter.PrecedenceNote.Should()
            .Contain("your rules beat both").And.Contain("never deleted");
    }
}
