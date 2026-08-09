namespace RimManager.Core.Domain;

/// <summary>
/// A point-in-time capture of a modlist's arrangement, for the History tab (<c>2d</c>).
/// <para>
/// History is <b>append-only</b> (design non-negotiable #5): "Restore this state" appends
/// a new snapshot whose contents equal the old one — it never rewinds. Nothing is ever
/// destroyed except by an explicit prune.
/// </para>
/// </summary>
public sealed record ModlistSnapshot
{
    public required string Id { get; init; }
    public required string ModlistId { get; init; }
    public DateTimeOffset TakenUtc { get; init; }

    /// <summary>Why it was taken, e.g. <c>apply</c>, <c>manual</c>, <c>pre-sort</c>, <c>switch</c>.</summary>
    public string Reason { get; init; } = "manual";

    /// <summary>A user-given name ("Before CE 1.7 test"). Naming is the whole of what
    /// makes a state durable — see <see cref="IsProtected"/>.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Named snapshots are exempt from pruning (<c>2d</c>) — the whole point of naming a
    /// state is that it survives the rolling window.
    /// <para>
    /// There used to be a second, independent <c>Pinned</c> flag with a ★ of its own, and
    /// it was removed (O26) because it never meant anything separate: pinning and naming
    /// produced the identical effect, so the UI showed two controls for one outcome and
    /// could not explain either. One identity, one control. Every stored snapshot carried
    /// <c>pinned: false</c>, so nothing needed migrating; the key is simply ignored if an
    /// older file still has it.
    /// </para>
    /// </summary>
    public bool IsProtected => !string.IsNullOrWhiteSpace(Name);

    public ModlistState State { get; init; } = ModlistState.Empty;
}
