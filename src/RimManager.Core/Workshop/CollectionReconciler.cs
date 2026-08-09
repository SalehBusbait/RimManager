using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Workshop;

/// <summary>One collection member, matched (or not) against the local install.</summary>
public sealed record CollectionMember
{
    public required string PublishedFileId { get; init; }

    /// <summary>Workshop title, when metadata was fetched for this member.</summary>
    public string? Title { get; init; }

    /// <summary>The installed mod's packageId, if this member is present on disk.</summary>
    public ModId? InstalledPackageId { get; init; }

    /// <summary>True if Steam reported the member as deleted/hidden (metadata result 9).</summary>
    public bool IsDelisted { get; init; }

    /// <summary>
    /// Download size from the same metadata the title came from, when known. The
    /// import screen (<c>2e</c>) has a SIZE column whose job is to answer "how much am
    /// I about to pull down"; an unknown size renders as a dash rather than a guess.
    /// </summary>
    public long? SizeBytes { get; init; }

    public bool IsInstalled => InstalledPackageId is not null;

    /// <summary>Best label available: installed mod name → workshop title → the id.</summary>
    public string DisplayName => Title ?? InstalledPackageId?.Display ?? PublishedFileId;
}

/// <summary>The reconciliation of a collection against what's installed.</summary>
public sealed record CollectionReport
{
    public required ImmutableArray<CollectionMember> Members { get; init; }

    public int InstalledCount => Members.Count(m => m.IsInstalled);

    public int MissingCount => Members.Count(m => !m.IsInstalled);

    public IEnumerable<CollectionMember> Missing => Members.Where(m => !m.IsInstalled);
}

/// <summary>
/// Pure reconciliation of a Workshop collection's members against the local scan,
/// keyed by <see cref="Mod.PublishedFileId"/> (a collection references items by
/// Workshop id, not <c>packageId</c>, so this can't reuse the <c>.rwlist</c>
/// reconciler). Optional metadata supplies titles for members — especially the
/// missing ones, which have no local name to show.
/// </summary>
public static class CollectionReconciler
{
    public static CollectionReport Reconcile(
        IEnumerable<string> memberIds,
        IReadOnlyDictionary<string, Mod> installedByFileId,
        IReadOnlyDictionary<string, WorkshopItem>? metadataById = null)
    {
        ArgumentNullException.ThrowIfNull(memberIds);
        ArgumentNullException.ThrowIfNull(installedByFileId);

        var members = ImmutableArray.CreateBuilder<CollectionMember>();
        foreach (var id in memberIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;

            installedByFileId.TryGetValue(id, out var installed);
            WorkshopItem? meta = metadataById is not null && metadataById.TryGetValue(id, out var m) ? m : null;

            members.Add(new CollectionMember
            {
                PublishedFileId = id,
                Title = meta is { IsOk: true } ? meta.Title : null,
                InstalledPackageId = installed?.PackageId,
                IsDelisted = meta is { IsOk: false },
                SizeBytes = meta is { IsOk: true } ? meta.FileSize : null,
            });
        }

        return new CollectionReport { Members = members.ToImmutable() };
    }

    /// <summary>Indexes scanned mods by their Workshop id (skipping those without one).</summary>
    public static Dictionary<string, Mod> IndexByFileId(IEnumerable<Mod> mods)
    {
        var map = new Dictionary<string, Mod>(StringComparer.Ordinal);
        foreach (var mod in mods)
        {
            if (mod.PublishedFileId is { } fid) map[fid] = mod;
        }

        return map;
    }
}
