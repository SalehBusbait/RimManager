namespace RimManager.Core.Workshop;

/// <summary>
/// Updates subscribed Workshop items <b>in place, through the Steam client</b> — the
/// subscription stays the owner of the mod, the client keeps its own bookkeeping
/// (<c>appworkshop_294100.acf</c>) correct, and nothing is copied into the local Mods
/// folder. This is the seam for the Steamworks-native path; the concrete
/// implementation lives in <c>RimManager.Integrations</c> because reaching the client
/// means loading a native library and talking IPC, which <c>Core</c> must not do.
/// </summary>
/// <remarks>
/// <para>The one earlier design this replaces, and why: a SteamCMD download into the
/// local Mods folder also "updates" a subscribed mod, but only by SHADOWING the
/// subscription (Local &gt; Workshop precedence) — the mod silently changes owner and
/// stops tracking Workshop updates. Update must mean "the subscription is now
/// current", not "you now own a copy".</para>
/// <para>The contract is <b>fire, then watch</b> — ask the client to download, close
/// the Steamworks session immediately, and observe completion from the outside (the
/// acf manifest). Holding the session open to poll <c>GetItemState</c> deadlocks,
/// found live: a session against the game's app id makes Steam believe the game is
/// running, and by default Steam pauses downloads during gameplay — so the client
/// waits for the "game" to exit while the poll waits for the download.
/// <see cref="WorkshopUpdateRequest.RemoteUpdatedUtc"/> is how the watcher knows an
/// item is done: the acf's installed-version timestamp reaching the remote publish
/// time is the exact comparison the update check itself is built on.</para>
/// </remarks>
public interface IWorkshopUpdater
{
    /// <summary>
    /// Asks the client to download the current version of each item, then watches
    /// until the downloads land or a deadline passes. One result per requested item,
    /// in request order. Failure is per-item (<see cref="WorkshopUpdateResult.Updated"/>
    /// false with a reason) except when the client itself is unreachable, which throws.
    /// </summary>
    Task<IReadOnlyList<WorkshopUpdateResult>> UpdateAsync(
        IReadOnlyList<WorkshopUpdateRequest> items,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>One item to update. <paramref name="RemoteUpdatedUtc"/> is the live
/// Workshop publish time from the update check — the watcher's finish line. Null means
/// "any forward movement of the installed timestamp counts".</summary>
public sealed record WorkshopUpdateRequest(string PublishedFileId, DateTimeOffset? RemoteUpdatedUtc);

/// <summary>One item's outcome. <paramref name="Detail"/> is a short human reason when
/// <paramref name="Updated"/> is false, or null on success.</summary>
public sealed record WorkshopUpdateResult(string PublishedFileId, bool Updated, string? Detail = null);
