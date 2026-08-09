using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sharing;

public enum ImportStatus
{
    /// <summary>Installed and ready.</summary>
    Installed,

    /// <summary>Listed but not installed — needs downloading.</summary>
    Missing,

    /// <summary>Installed, but a different version than the list records.</summary>
    VersionMismatch,
}

public sealed record ImportItem(
    ModId PackageId,
    string DisplayName,
    ImportStatus Status,
    string? ListedVersion,
    string? InstalledVersion);

/// <summary>The reconciliation of an imported list against what's installed (spec §4.7).</summary>
public sealed record ImportReport(ImmutableArray<ImportItem> Items)
{
    public int InstalledCount => Items.Count(i => i.Status == ImportStatus.Installed);
    public int MissingCount => Items.Count(i => i.Status == ImportStatus.Missing);
    public int VersionMismatchCount => Items.Count(i => i.Status == ImportStatus.VersionMismatch);
}

/// <summary>Compares an imported list to installed mods, classifying each entry.</summary>
public static class ImportReconciler
{
    public static ImportReport Reconcile(RwList list, IReadOnlyDictionary<ModId, Mod> installed)
    {
        var items = ImmutableArray.CreateBuilder<ImportItem>();
        foreach (var entry in list.Mods)
        {
            if (entry.PackageId is null) continue;
            var id = ModId.From(entry.PackageId);

            if (!installed.TryGetValue(id, out var mod))
            {
                items.Add(new ImportItem(id, entry.DisplayName ?? id.Display, ImportStatus.Missing, entry.ModVersion, null));
                continue;
            }

            var mismatch = entry.ModVersion is { } listed && mod.ModVersion is { } have
                           && !string.Equals(listed, have, StringComparison.Ordinal);

            items.Add(new ImportItem(
                id,
                entry.DisplayName ?? id.Display,
                mismatch ? ImportStatus.VersionMismatch : ImportStatus.Installed,
                entry.ModVersion,
                mod.ModVersion));
        }

        return new ImportReport(items.ToImmutable());
    }
}
