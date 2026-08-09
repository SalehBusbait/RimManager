using System.Security.Cryptography;
using System.Text;

namespace RimManager.Core.Domain;

/// <summary>How the selected modlist stands against what the game actually has.</summary>
public enum DriftKind
{
    /// <summary>The game's active order is exactly the list's. Nothing to say.</summary>
    InSync,

    /// <summary>
    /// The list moved and the game is still where RimManager left it — ordinary unsaved
    /// work, already reported by the commit bar. Not drift.
    /// </summary>
    PendingApply,

    /// <summary>
    /// <b>Something other than RimManager rewrote <c>ModsConfig.xml</c>.</b> Almost always
    /// RimWorld itself: loading a save offers to adopt that save's mod list, and accepting
    /// rewrites the file. The user has to be told, because under "the modlist is the truth"
    /// the next Apply would silently discard whatever the game just did.
    /// </summary>
    ChangedOutsideRimManager,

    /// <summary>
    /// The two disagree but RimManager has never applied this list, so there is no
    /// evidence about which of them moved. Reported as a question, never as an accusation.
    /// </summary>
    Unknown,
}

/// <summary>
/// Detects <c>ModsConfig.xml</c> changing underneath a modlist.
/// <para>
/// This matters <em>more</em> once modlists are the source of truth, not less. RimWorld
/// rewrites that file whenever a player accepts "load this save's mod list", and every
/// Steam update can change what is installed. Without this the app would keep showing a
/// stale arrangement and quietly overwrite the game's on the next Apply.
/// </para>
/// <para>
/// Pure, and keyed on a hash of the applied order rather than a timestamp: file mtimes
/// move for reasons that are not content changes, and this project has already been
/// burned once by inferring meaning from an mtime.
/// </para>
/// </summary>
public static class ModlistDrift
{
    /// <summary>
    /// A stable fingerprint of an active load order. Order is significant — that is the
    /// whole point of a load order — so this is not a set hash.
    /// </summary>
    public static string HashOrder(IEnumerable<ModId> ids)
    {
        var joined = string.Join('\n', ids.Select(i => i.Value));
        return System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }

    /// <summary>
    /// The order RimManager last wrote to the game <b>by any list</b> — the evidence
    /// <see cref="Classify"/> needs, and not the same thing as the selected list's own
    /// <see cref="Modlist.LastAppliedHash"/>.
    /// <para>
    /// Measured on a real install, which is the only reason this exists. Two lists:
    /// "Vanilla Plus" applied at 14:09, "Vanilla" applied at 00:54. Selecting "Vanilla"
    /// compared the game against <em>Vanilla's</em> stamp, found they differed, and
    /// concluded <see cref="DriftKind.ChangedOutsideRimManager"/> — accusing RimWorld of
    /// rewriting a file that <b>RimManager itself</b> had rewritten, seven hours ago, on
    /// purpose. The verdict that exists to warn about data loss was crying wolf on the
    /// ordinary act of switching lists.
    /// </para>
    /// <para>
    /// The question is "is the game where RimManager left it", and the whole app left it
    /// there, not one list. With that, switching to an unapplied list reads
    /// <see cref="DriftKind.PendingApply"/> — the list moved, the game did not — which is
    /// both true and actionable, and <see cref="DriftKind.ChangedOutsideRimManager"/> goes
    /// back to meaning what it says.
    /// </para>
    /// </summary>
    public static string? LastWrittenToGame(IEnumerable<Modlist> lists)
    {
        Modlist? latest = null;

        foreach (var list in lists)
        {
            // Never applied: carries no evidence about the game, and an unstamped list
            // must not outrank a stamped one just by appearing later.
            if (list.LastAppliedUtc is null || list.LastAppliedHash is null) continue;
            if (latest is null || list.LastAppliedUtc > latest.LastAppliedUtc) latest = list;
        }

        return latest?.LastAppliedHash;
    }

    /// <param name="lastAppliedHash">
    /// The order RimManager last wrote to the game — from <see cref="LastWrittenToGame"/>,
    /// not from the selected list alone. Null when nothing has ever been applied, which is
    /// not a failure, just an absence of evidence.
    /// </param>
    public static DriftKind Classify(
        ModlistState list, IEnumerable<ModId> gameOrder, string? lastAppliedHash)
    {
        var game = HashOrder(gameOrder);
        var wanted = HashOrder(list.ActiveModIds());

        if (game == wanted) return DriftKind.InSync;
        if (string.IsNullOrEmpty(lastAppliedHash)) return DriftKind.Unknown;

        // The game is exactly where we left it, so the thing that moved is the list.
        return game == lastAppliedHash ? DriftKind.PendingApply : DriftKind.ChangedOutsideRimManager;
    }

    /// <summary>
    /// What to tell the user, in the status bar. Empty for the two states that are not
    /// worth a sentence: in sync, and ordinary unsaved edits the commit bar already owns.
    /// </summary>
    public static string Describe(DriftKind kind, int gameCount, int listCount) => kind switch
    {
        DriftKind.ChangedOutsideRimManager =>
            $"RimWorld's mod list changed outside RimManager — the game has {gameCount} active, "
            + $"this list has {listCount}.",
        DriftKind.Unknown =>
            $"This list ({listCount} mods) does not match what the game has ({gameCount}). "
            + "It has never been applied, so which is newer is not known.",
        _ => string.Empty,
    };
}
