using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>Assigns each mod its hard <see cref="Tier"/> (spec §4.4).</summary>
public static class TierAssigner
{
    public static Tier Assign(Mod mod, RuleSet rules)
    {
        var id = mod.PackageId;

        // STRUCTURAL first, and deliberately ahead of the rule hints below. Harmony,
        // the base game and the expansions are facts about the install, not preferences:
        // a database entry must not be able to reorder RimWorld against its own DLC.
        // This also stops mattering the moment Top means "first among mods" — Top is by
        // definition a tier for mods, so it can never be the right answer for these.
        if (id == KnownMods.Harmony) return Tier.PreCore;
        if (mod.Source == ModSource.Core || id == KnownMods.Core) return Tier.Core;
        if (mod.Source == ModSource.Dlc || KnownMods.IsOfficialDlc(id)) return Tier.Dlc;

        // Then the explicit hints. Community and user rules beat an About.xml
        // declaration (rule precedence is About -> community -> user), so both of these
        // are decided before the mod's own claim about itself.
        if (rules.LoadTop.ContainsKey(id)) return Tier.Top;
        if (rules.LoadBottom.ContainsKey(id)) return Tier.Bottom;

        if (DeclaresItLoadsBeforeTheGame(mod)) return Tier.PreCore;

        return Tier.Normal;
    }

    /// <summary>
    /// Whether the mod's own <c>About.xml</c> says it loads before the base game.
    /// <para>
    /// This is the fix for a class of silent failure, not a special case for three
    /// mods. Loading before Core used to be recognised by <b>identity</b> — the single
    /// hardcoded id <c>brrainz.harmony</c> — so every other pre-patcher was assigned
    /// <see cref="Tier.Normal"/>, one tier BELOW Core. Its own
    /// <c>&lt;loadBefore&gt;Ludeon.RimWorld&lt;/loadBefore&gt;</c> then read as a rule
    /// ordering a later tier before an earlier one, which <see cref="ModSorter"/> drops
    /// by design — and <c>ModListValidator</c> reports only APPLIED edges, so nothing
    /// on screen ever said so.
    /// </para>
    /// <para>
    /// Measured on a real 548-mod install: Prepatcher, Loading Progress and Better
    /// Stacktraces all declare it, all sat below Core, and all four of their
    /// declarations were discarded without a word. Deriving the tier from the
    /// declaration means the next pre-patcher works with no list to maintain and no
    /// rule to add.
    /// </para>
    /// <para>
    /// Harmony counts as a target as well as Core: Harmony is itself pre-Core, so
    /// anything declaring it must load before Harmony is necessarily pre-Core too —
    /// which is exactly what Prepatcher declares.
    /// </para>
    /// </summary>
    private static bool DeclaresItLoadsBeforeTheGame(Mod mod) =>
        NamesAnAnchor(mod.LoadBefore) || NamesAnAnchor(mod.ForceLoadBefore);

    private static bool NamesAnAnchor(ImmutableArray<ModId> ids) =>
        !ids.IsDefaultOrEmpty && ids.Any(id => id == KnownMods.Core || id == KnownMods.Harmony);
}
