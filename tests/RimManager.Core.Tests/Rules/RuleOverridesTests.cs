using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;
using Xunit;

namespace RimManager.Core.Tests.Rules;

/// <summary>
/// The rule editor's backing store (2i-5): the user's own rules, and community rules
/// they have switched off.
/// </summary>
public sealed class RuleOverridesTests
{
    private static ModId Id(string s) => ModId.From(s);

    private static OrderingEdge Community(string before, string after) =>
        new(Id(before), Id(after), new RuleProvenance(RuleSource.Community, RuleType.LoadAfter));

    private static ImmutableArray<OrderingEdge> Edges(params (string before, string after)[] pairs) =>
        [.. pairs.Select(p => Community(p.before, p.after))];

    [Fact]
    public void Empty_changes_nothing()
    {
        var edges = Edges(("a", "b"), ("b", "c"));

        RuleOverrides.Empty.Apply(edges).Should().BeEquivalentTo(edges);
    }

    /// <summary>
    /// 2i-5: "Disabled community rules render at 55% opacity and are never deleted."
    /// The rule is recorded as an identity, so the row stays visible-but-off and a
    /// database resync cannot silently resurrect it.
    /// </summary>
    [Fact]
    public void A_disabled_rule_is_dropped_from_the_graph_but_kept_as_a_record()
    {
        var overrides = RuleOverrides.Empty.Disable(Id("a"), Id("b"), "wrong for my list");

        var applied = overrides.Apply(Edges(("a", "b"), ("b", "c")));

        applied.Should().ContainSingle().Which.Before.Should().Be(Id("b"));
        overrides.Disabled.Should().ContainSingle();
        overrides.IsDisabled(Id("a"), Id("b")).Should().BeTrue();
    }

    [Fact]
    public void Disabling_is_directional()
    {
        var overrides = RuleOverrides.Empty.Disable(Id("a"), Id("b"));

        overrides.IsDisabled(Id("b"), Id("a")).Should().BeFalse();
    }

    [Fact]
    public void Disabling_twice_records_one_entry() =>
        RuleOverrides.Empty.Disable(Id("a"), Id("b")).Disable(Id("a"), Id("b"))
            .Disabled.Should().ContainSingle();

    [Fact]
    public void Enable_restores_a_rule_that_was_only_ever_marked()
    {
        var overrides = RuleOverrides.Empty.Disable(Id("a"), Id("b")).Enable(Id("a"), Id("b"));

        overrides.IsDisabled(Id("a"), Id("b")).Should().BeFalse();
        overrides.Apply(Edges(("a", "b"))).Should().ContainSingle();
    }

    // --- user rules ----------------------------------------------------------

    /// <summary>2i-5: "Yours are accent-marked and always win."</summary>
    [Fact]
    public void A_user_rule_is_added_with_user_provenance()
    {
        var overrides = RuleOverrides.Empty.WithUserRule(new UserRule(Id("x"), Id("y")));

        var applied = overrides.Apply([]);

        applied.Should().ContainSingle();
        applied[0].Provenance.Source.Should().Be(RuleSource.User);
    }

    [Fact]
    public void A_user_rule_replaces_a_community_rule_for_the_same_pair()
    {
        var overrides = RuleOverrides.Empty.WithUserRule(new UserRule(Id("a"), Id("b")));

        var applied = overrides.Apply(Edges(("a", "b")));

        applied.Should().ContainSingle("a user rule replaces rather than duplicates");
        applied[0].Provenance.Source.Should().Be(RuleSource.User);
    }

    /// <summary>
    /// The ordering that matters: the user's rule is appended AFTER the disable
    /// filter, so re-adding a rule you had previously switched off actually works.
    /// Filtering last would make it silently do nothing.
    /// </summary>
    [Fact]
    public void A_user_rule_wins_even_over_the_users_own_disable_of_the_same_pair()
    {
        var overrides = RuleOverrides.Empty
            .Disable(Id("a"), Id("b"))
            .WithUserRule(new UserRule(Id("a"), Id("b")));

        var applied = overrides.Apply(Edges(("a", "b")));

        applied.Should().ContainSingle();
        applied[0].Provenance.Source.Should().Be(RuleSource.User);
    }

    [Fact]
    public void Adding_the_same_user_pair_twice_replaces_it()
    {
        var overrides = RuleOverrides.Empty
            .WithUserRule(new UserRule(Id("a"), Id("b"), "first"))
            .WithUserRule(new UserRule(Id("a"), Id("b"), "second"));

        overrides.UserRules.Should().ContainSingle().Which.Comment.Should().Be("second");
    }

    [Fact]
    public void WithoutUserRule_removes_it()
    {
        var overrides = RuleOverrides.Empty
            .WithUserRule(new UserRule(Id("a"), Id("b")))
            .WithoutUserRule(Id("a"), Id("b"));

        overrides.UserRules.Should().BeEmpty();
        overrides.IsEmpty.Should().BeTrue();
    }

    /// <summary>The count on the Settings ▸ Sorting rules card ("6 local overrides").</summary>
    [Fact]
    public void Override_count_covers_both_kinds()
    {
        var overrides = RuleOverrides.Empty
            .WithUserRule(new UserRule(Id("a"), Id("b")))
            .Disable(Id("c"), Id("d"));

        overrides.OverrideCount.Should().Be(2);
    }

    [Fact]
    public void Applying_to_an_empty_edge_set_is_safe() =>
        RuleOverrides.Empty.Apply([]).Should().BeEmpty();
}
