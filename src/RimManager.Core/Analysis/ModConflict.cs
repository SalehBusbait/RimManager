using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Analysis;

public enum ConflictKind
{
    /// <summary>Two or more mods define the same <c>defName</c> (last in load order wins).</summary>
    DefOverride,

    /// <summary>Two or more mods ship the same relative texture path (last wins).</summary>
    TextureCollision,

    /// <summary>Two or more mods patch the same XML node via a PatchOperation.</summary>
    PatchCollision,

    /// <summary>Two or more mods Harmony-patch the same method.</summary>
    HarmonyPatch,
}

/// <summary>
/// One mod's side of a conflict: where its version of the contested thing lives and,
/// where the analyzer could capture it, the XML itself.
/// <para>
/// The XML is what makes the two-up diff viewer (<c>3c</c>) possible. Without it the
/// Conflicts tab could only say "these four mods define Gun_AssaultRifle"; with it,
/// the user can see that CE changes <c>RangedWeapon_Cooldown</c> and adds
/// <c>Bulk</c>, which is the thing they actually need to decide with.
/// </para>
/// </summary>
/// <param name="Xml">
/// The contested element's own markup, not the whole file — a Defs file can be
/// thousands of lines and only one element is in contention. Null when the analyzer
/// works on file paths rather than XML (textures), or when the file could not be read.
/// </param>
public sealed record ConflictProvider(ModId ModId, string? SourceFile = null, string? Xml = null);

/// <summary>
/// A Tier-2 conflict (spec §4.5): several mods contend over the same thing. The
/// mods are listed in load order, and <see cref="Winner"/> is the one that takes
/// effect for override-style conflicts.
/// </summary>
public sealed record ModConflict(
    ConflictKind Kind,
    string Key,
    ImmutableArray<ModId> Mods,
    ModId Winner,
    string? Detail = null,
    ImmutableArray<ConflictProvider> Providers = default)
{
    /// <summary>Providers in load order; empty rather than default so callers need no guard.</summary>
    public ImmutableArray<ConflictProvider> ProvidersOrEmpty =>
        Providers.IsDefault ? [] : Providers;

    /// <summary>
    /// The two sides the XML diff opens with: the winner, and the last provider
    /// before it that was overwritten. Null when fewer than two carry XML.
    /// </summary>
    public (ConflictProvider Overwritten, ConflictProvider Wins)? DiffPair()
    {
        var withXml = ProvidersOrEmpty.Where(p => p.Xml is not null).ToArray();
        return withXml.Length < 2 ? null : (withXml[^2], withXml[^1]);
    }
}

public sealed record ConflictReport(ImmutableArray<ModConflict> Conflicts)
{
    public static readonly ConflictReport Empty = new([]);

    public int CountOf(ConflictKind kind) => Conflicts.Count(c => c.Kind == kind);
}
