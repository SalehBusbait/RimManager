using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Workshop;

/// <summary>
/// The three snooze options from the Updates dock tab (<c>2b</c>).
/// </summary>
public enum SnoozeKind
{
    /// <summary>Quiet for seven days, then it comes back.</summary>
    OneWeek,

    /// <summary>Quiet until a version newer than the one snoozed at appears.</summary>
    UntilNextVersion,

    /// <summary>
    /// Quiet until RimWorld itself moves on. <c>2b</c>: "The last is the one people
    /// actually want" — a mod you have deliberately held back is usually held back
    /// for the current game version, not for a week.
    /// </summary>
    UntilNextGameVersion,
}

/// <summary>
/// One snoozed mod. The version fields record what was true <em>when</em> the snooze
/// was set, which is what lets <see cref="UntilNextVersion"/> and
/// <see cref="UntilNextGameVersion"/> expire by comparison rather than by a timer.
/// </summary>
public sealed record ModSnooze(
    ModId ModId,
    SnoozeKind Kind,
    DateTimeOffset SnoozedUtc,
    string? AtModVersion = null,
    string? AtGameVersion = null)
{
    /// <summary>
    /// Whether this snooze still applies. <paramref name="latestVersion"/> is the
    /// version currently on offer and <paramref name="gameVersion"/> the installed
    /// RimWorld version.
    /// </summary>
    public bool IsActive(DateTimeOffset now, string? latestVersion, string? gameVersion) => Kind switch
    {
        SnoozeKind.OneWeek => now < SnoozedUtc.AddDays(7),

        // A null recorded version means we never knew what we were snoozing past, so
        // the snooze cannot expire by comparison — treat it as spent rather than
        // silently hiding the update forever.
        SnoozeKind.UntilNextVersion =>
            AtModVersion is not null
            && string.Equals(AtModVersion, latestVersion, StringComparison.OrdinalIgnoreCase),

        SnoozeKind.UntilNextGameVersion =>
            AtGameVersion is not null
            && string.Equals(AtGameVersion, gameVersion, StringComparison.OrdinalIgnoreCase),

        _ => false,
    };
}

/// <summary>
/// The persisted set of snoozed mods, keyed by packageId so a snooze survives a mod
/// being updated, moved between sources, or re-scanned.
/// </summary>
public sealed record SnoozeSet(ImmutableArray<ModSnooze> Entries)
{
    public static readonly SnoozeSet Empty = new([]);

    private ImmutableArray<ModSnooze> Safe => Entries.IsDefault ? [] : Entries;

    /// <summary>Sets or replaces the snooze for one mod — snoozing twice is not two snoozes.</summary>
    public SnoozeSet With(ModSnooze snooze) =>
        new([.. Safe.Where(e => e.ModId != snooze.ModId), snooze]);

    /// <summary>Un-snoozes a mod (the row's "unsnooze" action).</summary>
    public SnoozeSet Without(ModId id) => new([.. Safe.Where(e => e.ModId != id)]);

    public ModSnooze? For(ModId id) => Safe.FirstOrDefault(e => e.ModId == id);

    /// <summary>Whether a mod's update should currently be hidden from the Updates tab.</summary>
    public bool IsSnoozed(ModId id, DateTimeOffset now, string? latestVersion, string? gameVersion) =>
        For(id) is { } snooze && snooze.IsActive(now, latestVersion, gameVersion);

    /// <summary>
    /// Drops spent snoozes. Worth doing on load rather than letting them accumulate:
    /// a stale entry is invisible in the UI but would keep matching by packageId if
    /// the mod were reinstalled.
    /// </summary>
    public SnoozeSet Prune(DateTimeOffset now, Func<ModId, (string? version, string? gameVersion)> lookup) =>
        new([.. Safe.Where(e =>
        {
            var (version, gameVersion) = lookup(e.ModId);
            return e.IsActive(now, version, gameVersion);
        })]);
}
