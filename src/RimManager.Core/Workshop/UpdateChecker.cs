using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Workshop;

/// <summary>The outcome of comparing an installed mod against its live Workshop metadata.</summary>
public enum UpdateStatus
{
    /// <summary>Installed version's publish time matches (or is newer than) the live one.</summary>
    UpToDate,

    /// <summary>The Workshop has a newer publish time than what's installed.</summary>
    UpdateAvailable,

    /// <summary>The item resolved to not-found/banned on the Workshop — likely delisted.</summary>
    Delisted,

    /// <summary>Not comparable: no <c>PublishedFileId</c>, not in the local manifest, or a
    /// timestamp was missing on one side. Not Steam-tracked or not enough info to judge.</summary>
    NotTracked,
}

/// <summary>The per-mod result of an update check.</summary>
public sealed record ModUpdateStatus
{
    public required ModId Id { get; init; }

    public required string Name { get; init; }

    public string? PublishedFileId { get; init; }

    public required UpdateStatus Status { get; init; }

    /// <summary>Publish time of the installed version (from the local manifest), if known.</summary>
    public DateTimeOffset? InstalledUtc { get; init; }

    /// <summary>Publish time reported live by the Workshop, if known.</summary>
    public DateTimeOffset? RemoteUtc { get; init; }

    /// <summary>Where the mod came from, for the row's source badge (<c>2b</c>).</summary>
    public ModSource Source { get; init; } = ModSource.Workshop;

    /// <summary>
    /// The installed version from About.xml, if the author declared one.
    /// <para>
    /// There is deliberately no matching <c>LatestVersion</c> for a Workshop item.
    /// Steam publishes an update <em>time</em>, never a version string, so the only
    /// honest thing the LATEST column can show for a Workshop row is the publish
    /// date — which is what the PUBLISHED column already carries. Inventing a version
    /// there would be the kind of confident-looking fiction this table must not print.
    /// </para>
    /// </summary>
    public string? InstalledVersion { get; init; }

    /// <summary>Download size reported by the Workshop, if known.</summary>
    public long? SizeBytes { get; init; }

    public bool HasUpdate => Status == UpdateStatus.UpdateAvailable;
}

/// <summary>
/// Pure update detection: for each mod, compares the installed version's Steam publish
/// time (from <see cref="WorkshopInstallState"/>, i.e. <c>appworkshop_294100.acf</c>)
/// against the live one (from <see cref="SteamWorkshopClient"/>). No I/O and no HTTP —
/// the caller supplies both sides, so this is fully unit-testable and deterministic.
/// </summary>
/// <remarks>
/// The two timestamps are the same kind of value (Steam's <c>time_updated</c> for a
/// published file), so the comparison is exact — no reliance on filesystem mtimes,
/// which Steam doesn't set to the publish time and which unrelated operations perturb.
/// When either side is unknown the result is <see cref="UpdateStatus.NotTracked"/>
/// rather than a guess.
/// </remarks>
public static class UpdateChecker
{
    public static ImmutableArray<ModUpdateStatus> Check(
        IEnumerable<Mod> mods,
        WorkshopInstallState installed,
        IReadOnlyDictionary<string, WorkshopItem> remoteById)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(remoteById);

        var results = ImmutableArray.CreateBuilder<ModUpdateStatus>();
        foreach (var mod in mods)
        {
            results.Add(Evaluate(mod, installed, remoteById));
        }

        return results.ToImmutable();
    }

    private static ModUpdateStatus Evaluate(
        Mod mod,
        WorkshopInstallState installed,
        IReadOnlyDictionary<string, WorkshopItem> remoteById)
    {
        var installedTime = mod.PublishedFileId is { } fid ? installed.TryGet(fid)?.TimeUpdatedUtc : null;
        WorkshopItem? remote = mod.PublishedFileId is { } id && remoteById.TryGetValue(id, out var r) ? r : null;
        var remoteTime = remote is { IsOk: true } ? remote.TimeUpdatedUtc : null;

        var status = Classify(mod.PublishedFileId, remote, installedTime, remoteTime);

        return new ModUpdateStatus
        {
            Id = mod.PackageId,
            Name = mod.Name,
            PublishedFileId = mod.PublishedFileId,
            Status = status,
            InstalledUtc = installedTime,
            RemoteUtc = remoteTime,
            Source = mod.Source,
            InstalledVersion = mod.ModVersion,
            SizeBytes = remote is { IsOk: true } ? remote.FileSize : null,
        };
    }

    private static UpdateStatus Classify(
        string? publishedFileId,
        WorkshopItem? remote,
        DateTimeOffset? installedTime,
        DateTimeOffset? remoteTime)
    {
        if (publishedFileId is null) return UpdateStatus.NotTracked;

        // We asked Steam and it said the item is gone.
        if (remote is { IsOk: false }) return UpdateStatus.Delisted;

        // Need both sides to make a call; otherwise we don't pretend to know.
        if (installedTime is not { } local || remoteTime is not { } live) return UpdateStatus.NotTracked;

        return live > local ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
    }
}
