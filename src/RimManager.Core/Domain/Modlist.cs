namespace RimManager.Core.Domain;

/// <summary>
/// A named, switchable mod list: its ordered arrangement of mods and separators, the
/// ordering decisions the user made, and optionally the in-game mod settings that go
/// with it. <b>This is the thing the user switches between</b> — it replaces the
/// instance as the app's primary unit (see the modlist migration).
/// <para>
/// It is the former <c>Profile</c> promoted. Profiles already carried the active set,
/// the exact load order and the separator layout; what they lacked was being the thing
/// the app loads on startup. An instance, meanwhile, isolated nothing: every one pointed
/// at the same Config folder, so all of them wrote the same <c>ModsConfig.xml</c> and
/// shared the same mod settings and saves.
/// </para>
/// </summary>
public sealed record Modlist
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// The one list that cannot be deleted. Seeded on first run by adopting the game's
    /// current <c>ModsConfig.xml</c>, so it is never empty.
    /// <para>
    /// The flag is identity, not the name — a default list may be renamed ("Main
    /// playthrough") and stays the default. Pinning the name as well would be a second
    /// rule buying no extra safety.
    /// </para>
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>Read-only lists refuse edits (the former <c>Profile.Locked</c>).</summary>
    public bool Locked { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ModifiedUtc { get; init; }

    /// <summary>When this list was last switched to, for the modlist table's LAST USED.</summary>
    public DateTimeOffset? LastUsedUtc { get; init; }

    /// <summary>
    /// A fingerprint of the load order RimManager last wrote to the game
    /// (<see cref="ModlistDrift.HashOrder"/>). Null until this list has been applied.
    /// <para>
    /// It is what lets the app tell "I have unsaved edits" apart from "RimWorld rewrote
    /// ModsConfig.xml behind me" — two states that look identical from the file alone, and
    /// which need opposite responses. Hashed rather than timestamped because file mtimes
    /// move for reasons that are not content changes.
    /// </para>
    /// </summary>
    public string? LastAppliedHash { get; init; }

    /// <summary>When this list was last written to the game.</summary>
    public DateTimeOffset? LastAppliedUtc { get; init; }

    /// <summary>
    /// The list's colour dot, as an index into <see cref="Palette"/> — never a hex
    /// (non-negotiable #6), so it resolves differently in light and dark.
    /// </summary>
    public int PaletteIndex { get; init; }

    /// <summary>
    /// The game version this list was last arranged against, e.g. <c>1.6</c>. Advisory:
    /// RimWorld tolerates version mismatch — <c>loadFolders</c>, XML and texture mods
    /// commonly work across versions and the game warns rather than fails — so this
    /// informs a warning, never a refusal to load.
    /// </summary>
    public string? GameVersion { get; init; }

    /// <summary>The arrangement: ordered mods (with enable state) and separators.</summary>
    public ModlistState State { get; init; } = ModlistState.Empty;

    /// <summary>
    /// Ordering edges the user chose to drop, from the Warnings detail panel or the cycle
    /// graph. Persisted so a cycle resolves the same way on every later sort (<c>3b</c>).
    /// </summary>
    public Sorting.EdgeSuppressions Suppressions { get; init; } = Sorting.EdgeSuppressions.Empty;

    /// <summary>
    /// Whether switching to this list also restores the in-game mod settings captured
    /// with it (<c>Config/Mod_*.xml</c>).
    /// <para>
    /// Opt-in per list, and off by default. Mod settings are the one thing an instance
    /// could genuinely have isolated, but capturing them silently would mean a switch
    /// rewrites files the user never asked us to touch.
    /// </para>
    /// </summary>
    public bool CapturesModSettings { get; init; }
}
