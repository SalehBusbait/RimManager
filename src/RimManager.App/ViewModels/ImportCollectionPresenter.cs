using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace RimManager.App.ViewModels;

/// <summary>
/// How an imported collection joins the load order — <c>2i</c>-3's three radios. The
/// choice is made in the modal (step 1) and carried out from the Collection tab
/// (step 2), so it is stored once, on <see cref="CollectionViewModel"/>, and both
/// surfaces read that one value.
/// </summary>
public enum ImportStrategy
{
    /// <summary>
    /// The default. Existing order untouched; new mods land under one separator named
    /// after the collection, so an import that turned out to be a mistake is undoable
    /// by eye rather than by memory.
    /// </summary>
    AppendGroup,

    /// <summary>Append with no separator, then run a full sort.</summary>
    MergeAndSort,

    /// <summary>Deactivate everything this collection does not name, then append it.</summary>
    Replace,

    /// <summary>
    /// A new modlist from the ticked members you already have, in collection order —
    /// the current list untouched (NF-10's landing, offered here for coherence: both
    /// sharing vehicles arrive the same way). Missing members still download or
    /// subscribe per the chosen route; a collection names mods by Workshop id only,
    /// so a member you don't have yet cannot be a list entry until it is installed.
    /// </summary>
    NewModlist,
}

/// <summary>
/// How the missing members are obtained. Two genuinely different outcomes, which is
/// why the user picks rather than the app deciding: subscribing leaves mods
/// Steam-managed and auto-updating in the Workshop folder, while SteamCMD leaves an
/// unmanaged copy in <c>Mods/</c> that only RimManager tracks.
/// </summary>
public enum ImportRoute
{
    /// <summary>
    /// Hand the collection to the Steam client for its native "Subscribe to all".
    /// The default when the client is running. Today this is a deep-link hand-off;
    /// a Steamworks <c>SubscribeItem</c> would be a better implementation of the same
    /// choice, not a new one.
    /// </summary>
    SubscribeInSteam,

    /// <summary>
    /// Anonymous SteamCMD. No account, no subscription, works with Steam closed — and
    /// the only route that honours a partial selection.
    /// </summary>
    SteamCmd,
}

/// <summary>
/// Avalonia-free logic behind the import-collection modal (<c>2i</c>-3): the sentences
/// it puts on screen and the one predicate that decides what "Replace my load order"
/// would deactivate.
/// <para>
/// The predicate lives here rather than in the command because the modal <i>states a
/// number</i> ("Deactivates the 155 mods not in this collection") that the button then
/// has to honour exactly. Two implementations of that would drift, and the sentence
/// would quietly become a lie.
/// </para>
/// </summary>
public static class ImportCollectionPresenter
{
    /// <summary>
    /// The confirmation line under the URL field: "Anomaly Essentials · 68 items ·
    /// updated 3 days ago". The ✓ is drawn as an icon by the view, never as a glyph
    /// in this string (non-negotiable #12).
    /// </summary>
    /// <remarks>
    /// <b>No author.</b> The design shows "· by Kaelith", but Steam's keyless
    /// <c>GetPublishedFileDetails</c> returns <c>creator</c> as a SteamID64; turning
    /// that into a display name needs <c>ISteamUser/GetPlayerSummaries</c> and a Web
    /// API key, which this project deliberately does not require. A raw 17-digit id
    /// would be worse than the absence.
    /// </remarks>
    public static string Resolved(string? title, int items, DateTimeOffset? updated, DateTimeOffset now)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "Untitled collection" : title.Trim();
        var line = $"{name} · {Items(items)}";
        return updated is null ? line : $"{line} · updated {UpdatesPresenter.Published(updated, now)}";
    }

    /// <summary>
    /// Step 1's primary button: "Review 68 items →". The arrow is the design's, and it
    /// earns its place on a two-step wizard: it says this button advances rather than
    /// commits, which is the promise the footer beside it makes in words.
    /// </summary>
    public static string ReviewLabel(int items) => $"Review {Items(items)} →";

    /// <summary>
    /// Step 2's primary. It enumerates rather than saying "Import", because the two
    /// acts behind it cost very different things: adding installed mods is instant,
    /// and a SteamCMD batch is minutes of network. A button that hides which of the
    /// two it is about to do is one nobody can predict.
    /// </summary>
    /// <param name="totalItems">
    /// Used only by the subscribe route, and that is the point: Steam's "Subscribe to
    /// all" takes the <b>whole</b> collection — it has no notion of our checkboxes — so
    /// the button says 476 rather than the ticked 343. A primary that quoted the
    /// selection would be describing something that is not going to happen.
    /// </param>
    public static string CommitLabel(
        int download, int add, ImportStrategy strategy, ImportRoute route, int totalItems)
    {
        if (download > 0 && route == ImportRoute.SubscribeInSteam)
        {
            return add > 0
                ? $"Subscribe to all {totalItems} · add {add}"
                : $"Subscribe to all {totalItems} in Steam";
        }

        return (download, add) switch
        {
            ( > 0, > 0) => strategy == ImportStrategy.NewModlist
                ? $"Download {download} · new modlist of {add}"
                : $"Download {download} · add {add}",
            ( > 0, 0) => $"Download {download} via SteamCMD",
            (0, > 0) => strategy switch
            {
                ImportStrategy.Replace => $"Replace with {add}",
                ImportStrategy.NewModlist => $"New modlist of {add}",
                _ => $"Add {add} to the load order",
            },
            _ => strategy == ImportStrategy.Replace
                ? "Replace the load order"
                : "Nothing selected",
        };
    }

    /// <summary>
    /// The consequence line on the Replace radio. It names what goes <i>and</i> what
    /// stays — the same shape as the destructive confirm (<c>2i</c>-6), because this
    /// radio is the only one of the three that removes anything.
    /// </summary>
    public static string ReplaceConsequence(int deactivating) => deactivating == 0
        ? "Nothing would be deactivated — everything you have loaded is already in this collection."
        : $"Deactivates the {deactivating} mod{(deactivating == 1 ? "" : "s")} not in this collection. "
          + "Core and DLC stay. Reversible via snapshot.";

    /// <summary>
    /// The active rows "Replace my load order" would move out: every mod the collection
    /// does not name, except the anchors.
    /// <para>
    /// Core, the DLC and pinned mods are excluded because deactivating Core stops the
    /// game from starting, and no Workshop collection lists them — so a literal reading
    /// of "not in this collection" would break the install on the most destructive of
    /// the three options.
    /// </para>
    /// </summary>
    public static ImmutableArray<ModRowViewModel> WouldDeactivate(
        IEnumerable<RowViewModel> activeRows, IReadOnlySet<string> memberFileIds)
    {
        ArgumentNullException.ThrowIfNull(activeRows);
        ArgumentNullException.ThrowIfNull(memberFileIds);

        return
        [
            .. activeRows.OfType<ModRowViewModel>()
                .Where(r => !r.IsAnchor)
                .Where(r => r.Mod.PublishedFileId is not { } id || !memberFileIds.Contains(id))
        ];
    }

    /// <summary>
    /// Star weights for the four-segment bar, in the order the counts are labelled.
    /// Proportional to the real counts, unlike the mockup's bar, which is drawn to a
    /// scale its own numbers do not support — a bar whose segments disagree with the
    /// figures beside them is worse than no bar.
    /// </summary>
    public static ImmutableArray<double> BarShares(
        int installed, int toDownload, int unavailable, int alreadyActive)
    {
        var total = installed + toDownload + unavailable + alreadyActive;
        return total <= 0 ? [0, 0, 0, 0] : [installed, toDownload, unavailable, alreadyActive];
    }

    private static string Items(int n) => $"{n} item{(n == 1 ? "" : "s")}";
}
