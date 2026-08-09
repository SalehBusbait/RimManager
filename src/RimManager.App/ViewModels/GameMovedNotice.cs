using System.Collections.Generic;
using System.Linq;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// The game-moved strip's decision logic (N5b): offer or not, which words, which name.
/// <para>
/// Beside <see cref="DriftIndicator"/> and Avalonia-free for the same reason it is:
/// <c>MainWindowViewModel</c> cannot be constructed under test, so anything decided
/// inside it is guarded by nothing. The strip itself is geometry; everything a test
/// can hold is here.
/// </para>
/// <para>
/// The vocabulary is RimWorld's own. The user who caused this state clicked
/// <b>"Load mod list from save"</b> in a dialog titled "Mods mismatch", so <em>"the
/// game's order"</em> is the phrase that connects our sentence to the one they just
/// read — never Reload, Revert, Sync, Update, Save or Discard: the first three name
/// no direction, Update is spent on Workshop updates, and Save/Discard name the
/// manager's intent while the user reads their own.
/// </para>
/// </summary>
public sealed record GameMovedNotice
{
    public bool Show { get; init; }

    /// <summary>
    /// <c>Auto-reset mods config on crash</c> — a real, default-on 1.6 option that leaves
    /// <c>activeMods</c> as Core alone. A state nobody chose: the strip explains it and
    /// offers Apply, and does <b>not</b> offer to adopt, because adopting a one-mod list
    /// there would be the app agreeing with an accident.
    /// </summary>
    public bool IsCrashReset { get; init; }

    /// <summary>
    /// The list carries edits never applied to the game (T5, S-STRIPS): "Replace this
    /// list" then knowingly discards those too, so the strip demotes it to text-3.
    /// False for the crash reset — that list is untouched by construction.
    /// </summary>
    public bool IsDirty { get; init; }

    public string Headline { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public static GameMovedNotice Hidden { get; } = new();

    /// <param name="listAppliedHash">
    /// The selected list's <b>own</b> applied hash — the input <c>Classify</c> does not
    /// receive, and the clean/dirty split every editor implements. A list never applied
    /// cannot answer and falls to the dirty side, because the cautious default is the
    /// safe one.
    /// </param>
    public static GameMovedNotice Decide(
        DriftKind drift,
        IReadOnlyList<ModId> gameOrder,
        IReadOnlyList<ModId> listOrder,
        string? listAppliedHash,
        Func<ModId, bool> isInstalled)
    {
        ArgumentNullException.ThrowIfNull(gameOrder);
        ArgumentNullException.ThrowIfNull(listOrder);
        ArgumentNullException.ThrowIfNull(isInstalled);

        // Only the state where something other than RimManager rewrote the file.
        // PendingApply is the commit bar's, Unknown is the footer's, InSync is nobody's.
        if (drift != DriftKind.ChangedOutsideRimManager) return Hidden;

        // The crash reset: nothing third-party left active, against a list that had real
        // mods. Both halves matter — a bare game against a bare list is an ordinary
        // external edit and gets the ordinary words.
        var gameIsBare = gameOrder.All(IsLudeon);
        var listHasMods = listOrder.Any(id => !IsLudeon(id));

        if (gameIsBare && listHasMods)
        {
            var whatIsLeft = gameOrder.Count switch
            {
                0 => "nothing active",
                1 => "only Core active",
                _ => "only Core and its expansions active",
            };

            return new GameMovedNotice
            {
                Show = true,
                IsCrashReset = true,
                Headline = "RimWorld reset its mod list",
                Detail = $"The game now has {whatIsLeft} — RimWorld's automatic reset "
                    + "after a crash, not a decision anyone made. This list is untouched; "
                    + "Apply to game restores it.",
            };
        }

        var sentences = new List<string>(4);

        // RimWorld's own dialog distinguishes an order-only change ("No mods have been
        // added or removed, but the order of your mod list has changed"), so we do too.
        var orderOnly = gameOrder.Count == listOrder.Count
            && gameOrder.ToHashSet().SetEquals(listOrder);

        sentences.Add(orderOnly
            ? "No mods were added or removed — only the order changed, usually RimWorld "
                + "loading a save's mod list."
            : $"The game has {gameOrder.Count} active, this list has {listOrder.Count} — "
                + "usually RimWorld loading a save's mod list.");

        // Truth one: ids the game names that resolve to nothing on disk. FromGame keeps
        // them on purpose, so they must be reported here rather than discovered at 2am.
        var missing = gameOrder.Count(id => !isInstalled(id));
        if (missing > 0)
        {
            sentences.Add(missing == 1
                ? "1 of the game's mods is not installed."
                : $"{missing} of the game's mods are not installed.");
        }

        // The clean/dirty split. Dirty means this list ALSO carries edits the game never
        // saw — replacing it sets those aside as well as the arrangement.
        var listIsClean = listAppliedHash is not null
            && ModlistDrift.HashOrder(listOrder) == listAppliedHash;
        if (!listIsClean)
            sentences.Add("This list also has edits never applied to the game.");

        // Truth two: the game's file is flat, so adopting cannot bring separators with
        // it. And unlike Vortex — whose wiki warns that neither of its choices can be
        // undone — ours can, so the copy says so.
        sentences.Add(
            "The adopted order arrives flat — ModsConfig.xml carries no separators — "
            + "and either choice leaves a restorable snapshot in History.");

        return new GameMovedNotice
        {
            Show = true,
            IsDirty = !listIsClean,
            Headline = "RimWorld's mod list changed outside RimManager",
            Detail = string.Join(" ", sentences),
        };
    }

    /// <summary>
    /// The name for a modlist adopted from the game — "RimWorld · 7 Aug 14:09", the same
    /// shape as the pre-apply snapshot label, so the two records of the same fact read
    /// as siblings. Unique against the existing names by suffixing, though colliding
    /// within one minute takes deliberate effort.
    /// </summary>
    public static string SuggestedName(DateTimeOffset now, IEnumerable<string> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var basis = $"RimWorld · {now:d MMM HH:mm}";
        if (!taken.Contains(basis)) return basis;

        for (var n = 2; ; n++)
        {
            var candidate = $"{basis} ({n})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static bool IsLudeon(ModId id) => id == KnownMods.Core || KnownMods.IsOfficialDlc(id);
}
