using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using Xunit;

namespace RimManager.Core.Tests.Sorting;

public sealed class TierAssignerTests
{
    private static RuleSet WithHints(
        (string id, bool top)[] hints)
    {
        var top = ImmutableDictionary.CreateBuilder<ModId, RuleProvenance>();
        var bottom = ImmutableDictionary.CreateBuilder<ModId, RuleProvenance>();
        foreach (var (id, isTop) in hints)
        {
            var prov = new RuleProvenance(RuleSource.Community, isTop ? RuleType.LoadTop : RuleType.LoadBottom);
            (isTop ? top : bottom)[ModId.From(id)] = prov;
        }

        return new RuleSet([], top.ToImmutable(), bottom.ToImmutable());
    }

    [Fact]
    public void Assigns_structural_tiers()
    {
        var rules = WithHints([]);
        TierAssigner.Assign(SortFixtures.Mod("brrainz.harmony"), rules).Should().Be(Tier.PreCore);
        TierAssigner.Assign(SortFixtures.Mod("ludeon.rimworld", ModSource.Core), rules).Should().Be(Tier.Core);
        TierAssigner.Assign(SortFixtures.Mod("ludeon.rimworld.royalty", ModSource.Dlc), rules).Should().Be(Tier.Dlc);
        TierAssigner.Assign(SortFixtures.Mod("some.mod"), rules).Should().Be(Tier.Normal);
    }

    [Fact]
    public void Dlc_is_recognized_by_package_id_even_without_source()
    {
        TierAssigner.Assign(SortFixtures.Mod("ludeon.rimworld.biotech"), WithHints([])).Should().Be(Tier.Dlc);
    }

    [Fact]
    public void LoadTop_and_loadBottom_hints_apply()
    {
        TierAssigner.Assign(SortFixtures.Mod("x.top"), WithHints([("x.top", true)])).Should().Be(Tier.Top);
        TierAssigner.Assign(SortFixtures.Mod("x.bot"), WithHints([("x.bot", false)])).Should().Be(Tier.Bottom);
    }

    /// <summary>
    /// Structural tiers beat an explicit hint, which is the reverse of what this once
    /// asserted. Harmony, the base game and the expansions are facts about the install,
    /// not preferences: a database entry must not be able to reorder RimWorld against
    /// its own DLC.
    /// <para>
    /// It also stopped being a meaningful case once <see cref="Tier.Top"/> was corrected
    /// to mean "first among MODS". Top now sits below Core and the DLC, so honouring a
    /// loadTop on the base game would push it BELOW its own expansions — the opposite of
    /// what the hint is for.
    /// </para>
    /// </summary>
    [Fact]
    public void A_load_top_hint_cannot_move_the_base_game_or_its_expansions()
    {
        TierAssigner.Assign(
                SortFixtures.Mod("ludeon.rimworld", ModSource.Core), WithHints([("ludeon.rimworld", true)]))
            .Should().Be(Tier.Core);

        TierAssigner.Assign(
                SortFixtures.Mod("ludeon.rimworld.royalty", ModSource.Dlc),
                WithHints([("ludeon.rimworld.royalty", false)]))
            .Should().Be(Tier.Dlc);
    }

    /// <summary>
    /// Top is first among MODS, not first outright — the ordering the community
    /// database means and the one it documents. A loadTop framework loads after the base
    /// game and its expansions, and before everything else.
    /// </summary>
    [Fact]
    public void A_load_top_mod_sorts_after_the_game_and_before_ordinary_mods()
    {
        ((int)Tier.PreCore).Should().BeLessThan((int)Tier.Core);
        ((int)Tier.Core).Should().BeLessThan((int)Tier.Dlc);
        ((int)Tier.Dlc).Should().BeLessThan((int)Tier.Top,
            "loadTop means top of the mods; the database's own comment is "
            + "\"as high up as possible after DLC\"");
        ((int)Tier.Top).Should().BeLessThan((int)Tier.Normal);
        ((int)Tier.Normal).Should().BeLessThan((int)Tier.Bottom);
    }

    /// <summary>
    /// The root cause of a real, silent failure. Loading before the base game was
    /// recognised by IDENTITY — the single id <c>brrainz.harmony</c> — when it is a
    /// property a mod DECLARES about itself. Every other pre-patcher landed in
    /// <see cref="Tier.Normal"/>, one tier below Core, which turned its own
    /// <c>loadBefore</c> into a tier violation: <see cref="ModSorter"/> drops those by
    /// design, and <c>ModListValidator</c> reported only applied edges, so nothing on
    /// screen ever said so.
    /// <para>
    /// Measured on a real 548-mod install: Prepatcher, Loading Progress and Better
    /// Stacktraces all declare it, all sat below Core, and four declarations were
    /// discarded without a word.
    /// </para>
    /// </summary>
    [Fact]
    public void A_mod_declaring_it_loads_before_core_is_pre_core()
    {
        var rules = WithHints([]);

        TierAssigner.Assign(
                SortFixtures.Mod("zetrith.prepatcher", loadBefore: ["Ludeon.RimWorld", "brrainz.harmony"]),
                rules)
            .Should().Be(Tier.PreCore);

        TierAssigner.Assign(
                SortFixtures.Mod("ilyvion.loadingprogress", loadBefore: ["ludeon.rimworld"]), rules)
            .Should().Be(Tier.PreCore);
    }

    /// <summary>
    /// Harmony counts as an anchor as well as Core: Harmony is itself pre-Core, so a
    /// mod that must load before Harmony is necessarily before Core too — which is
    /// exactly what Prepatcher declares.
    /// </summary>
    [Fact]
    public void Declaring_it_loads_before_harmony_is_enough()
    {
        TierAssigner.Assign(
                SortFixtures.Mod("some.prepatcher", loadBefore: ["brrainz.harmony"]), WithHints([]))
            .Should().Be(Tier.PreCore);
    }

    /// <summary>forceLoadBefore is the same claim, stated more strongly.</summary>
    [Fact]
    public void A_forced_declaration_counts_the_same()
    {
        TierAssigner.Assign(
                SortFixtures.Mod("some.prepatcher", forceLoadBefore: ["Ludeon.RimWorld"]), WithHints([]))
            .Should().Be(Tier.PreCore);
    }

    /// <summary>
    /// It is a claim about loading BEFORE the game, so the opposite claim must not
    /// promote anything — otherwise the hundreds of mods that declare
    /// <c>loadAfter Ludeon.RimWorld</c> would all be hoisted above Core.
    /// </summary>
    [Fact]
    public void Declaring_it_loads_AFTER_core_promotes_nothing()
    {
        TierAssigner.Assign(
                SortFixtures.Mod("ordinary.mod", loadAfter: ["ludeon.rimworld"]), WithHints([]))
            .Should().Be(Tier.Normal);
    }

    /// <summary>
    /// Community and user rules beat an About.xml declaration (rule precedence is
    /// About -> community -> user), so an explicit loadBottom still wins over a mod's
    /// own claim to be pre-Core.
    /// </summary>
    [Fact]
    public void An_explicit_load_bottom_override_beats_the_declaration()
    {
        TierAssigner.Assign(
                SortFixtures.Mod("contrary.mod", loadBefore: ["ludeon.rimworld"]),
                WithHints([("contrary.mod", false)]))
            .Should().Be(Tier.Bottom);
    }

    /// <summary>Core and the DLC keep their own tiers whatever they declare.</summary>
    [Fact]
    public void The_base_game_cannot_demote_or_promote_itself()
    {
        var rules = WithHints([]);

        TierAssigner.Assign(
                SortFixtures.Mod("ludeon.rimworld", ModSource.Core, loadBefore: ["brrainz.harmony"]), rules)
            .Should().Be(Tier.Core);

        TierAssigner.Assign(
                SortFixtures.Mod("ludeon.rimworld.royalty", ModSource.Dlc, loadBefore: ["ludeon.rimworld"]), rules)
            .Should().Be(Tier.Dlc);
    }
}
