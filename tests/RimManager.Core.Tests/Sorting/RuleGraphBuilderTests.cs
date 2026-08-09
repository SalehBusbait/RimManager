using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;
using Xunit;
using static RimManager.Core.Tests.Sorting.SortFixtures;

namespace RimManager.Core.Tests.Sorting;

public sealed class RuleGraphBuilderTests
{
    private static LoadOrderRules Db(params (string id, ModRules rules)[] entries) =>
        new(entries.ToImmutableDictionary(e => ModId.From(e.id), e => e.rules));

    [Fact]
    public void About_load_after_becomes_edge_from_other_to_self()
    {
        var mods = new[] { Mod("a.a"), Mod("a.b", loadAfter: ["a.a"]) };

        var set = RuleGraphBuilder.Build(mods);

        set.Edges.Should().ContainSingle();
        set.Edges[0].Before.Should().Be(ModId.From("a.a"));
        set.Edges[0].After.Should().Be(ModId.From("a.b"));
    }

    [Fact]
    public void Rules_referencing_absent_mods_are_ignored()
    {
        var mods = new[] { Mod("a.a", loadAfter: ["not.installed"]) };
        RuleGraphBuilder.Build(mods).Edges.Should().BeEmpty();
    }

    /// <summary>
    /// Sources ADD to each other. A community rule about {a,b} used to delete the mod
    /// author's rule about the same pair; now both survive and the contradiction is a
    /// cycle, which the Cycles category exists to show and explain.
    /// <para>
    /// Measured before the change, on a 548-mod install against 629 community rules:
    /// no pair anywhere had sources disagreeing on direction. Twenty pairs carried
    /// several raw edges and all twenty agreed. So overriding was discarding nothing in
    /// practice — it was simply the wrong rule, waiting for the day two sources differ.
    /// </para>
    /// </summary>
    [Fact]
    public void Contradicting_sources_both_survive_as_a_cycle()
    {
        // About: b loads after a. Community: a loads after b. Genuinely opposite.
        var mods = new[] { Mod("a.a"), Mod("a.b", loadAfter: ["a.a"]) };
        var community = Db(("a.a", new ModRules { LoadAfter = [new RuleRef(ModId.From("a.b"))] }));

        var set = RuleGraphBuilder.Build(mods, community);

        set.Edges.Should().HaveCount(2, "neither source deletes the other");
        set.Edges.Should().Contain(e => e.Before == ModId.From("a.a") && e.After == ModId.From("a.b"));
        set.Edges.Should().Contain(e => e.Before == ModId.From("a.b") && e.After == ModId.From("a.a"));
    }

    /// <summary>
    /// Sources that AGREE collapse to one edge, attributed to the strongest of them —
    /// the claim a user editing rules would go looking for. This is the common case:
    /// every one of the twenty multi-source pairs on a real install is this shape.
    /// </summary>
    [Fact]
    public void Agreeing_sources_collapse_to_one_edge_named_by_the_strongest()
    {
        var mods = new[] { Mod("a.a"), Mod("a.b", loadAfter: ["a.a"]) };
        var community = Db(("a.b", new ModRules { LoadAfter = [new RuleRef(ModId.From("a.a"))] }));

        var set = RuleGraphBuilder.Build(mods, community);

        set.Edges.Should().ContainSingle();
        set.Edges[0].Provenance.Source.Should().Be(RuleSource.Community);
    }

    [Fact]
    public void Same_precedence_contradiction_is_preserved_as_two_edges()
    {
        // Two About rules contradict: a loadAfter b AND b loadAfter a. Keep both -> a cycle.
        var mods = new[] { Mod("a.a", loadAfter: ["a.b"]), Mod("a.b", loadAfter: ["a.a"]) };

        var set = RuleGraphBuilder.Build(mods);

        set.Edges.Should().HaveCount(2, "a genuine same-precedence contradiction must not be collapsed");
    }

    [Fact]
    public void User_load_top_hint_wins_over_community_load_bottom()
    {
        var mods = new[] { Mod("x.m") };
        var community = Db(("x.m", new ModRules { LoadBottom = true }));
        var user = Db(("x.m", new ModRules { LoadTop = true }));

        var set = RuleGraphBuilder.Build(mods, community, user);

        set.LoadTop.Should().ContainKey(ModId.From("x.m"));
        set.LoadBottom.Should().NotContainKey(ModId.From("x.m"));
    }

    // --- the rule editor's overrides, applied at the one point every consumer
    //     passes through (they shipped unwired once; RuleSourceParityTests guards
    //     the call sites, these guard the semantics) ---------------------------

    [Fact]
    public void A_disabled_community_rule_builds_no_edge()
    {
        var mods = new[] { Mod("a.a"), Mod("a.b") };
        var community = Db(("a.b", new ModRules { LoadAfter = [new RuleRef(ModId.From("a.a"))] }));
        var overrides = RuleOverrides.Empty.Disable(ModId.From("a.a"), ModId.From("a.b"));

        RuleGraphBuilder.Build(mods, community, overrides: overrides)
            .Edges.Should().BeEmpty("switching a rule off must actually switch it off");
    }

    [Fact]
    public void A_user_rule_becomes_an_edge_with_user_provenance()
    {
        var mods = new[] { Mod("a.a"), Mod("a.b") };
        var overrides = RuleOverrides.Empty
            .WithUserRule(new UserRule(ModId.From("a.a"), ModId.From("a.b")));

        var set = RuleGraphBuilder.Build(mods, overrides: overrides);

        set.Edges.Should().ContainSingle();
        set.Edges[0].Provenance.Source.Should().Be(RuleSource.User,
            "the editor's promise is that yours are accent-marked and always win");
    }

    [Fact]
    public void A_user_rule_about_an_absent_mod_orders_no_phantoms()
    {
        var mods = new[] { Mod("a.a") };
        var overrides = RuleOverrides.Empty
            .WithUserRule(new UserRule(ModId.From("a.a"), ModId.From("not.installed")));

        RuleGraphBuilder.Build(mods, overrides: overrides).Edges.Should().BeEmpty(
            "every other source is scoped to the active list, and the user's is no exception");
    }

    [Fact]
    public void A_user_rule_survives_the_users_own_disable_of_the_same_pair()
    {
        var mods = new[] { Mod("a.a"), Mod("a.b") };
        var community = Db(("a.b", new ModRules { LoadAfter = [new RuleRef(ModId.From("a.a"))] }));
        var overrides = RuleOverrides.Empty
            .Disable(ModId.From("a.a"), ModId.From("a.b"))
            .WithUserRule(new UserRule(ModId.From("a.a"), ModId.From("a.b")));

        var set = RuleGraphBuilder.Build(mods, community, overrides: overrides);

        set.Edges.Should().ContainSingle(
            "re-adding a rule you had switched off must not silently do nothing");
        set.Edges[0].Provenance.Source.Should().Be(RuleSource.User);
    }
}
