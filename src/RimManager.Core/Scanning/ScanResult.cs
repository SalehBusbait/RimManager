using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Scanning;

/// <summary>
/// The outcome of a scan: every discovered mod (deduped by packageId) plus any
/// scan-level warnings (e.g. duplicate packageId across sources).
/// </summary>
public sealed record ScanResult(
    ImmutableArray<Mod> Mods,
    ImmutableArray<ModWarning> Warnings)
{
    /// <summary>Index of mods by their canonical id for O(1) lookup.</summary>
    public ImmutableDictionary<ModId, Mod> ById { get; } =
        Mods.ToImmutableDictionary(m => m.PackageId);
}
