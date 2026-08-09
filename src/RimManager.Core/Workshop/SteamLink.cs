using System.Collections.Immutable;

namespace RimManager.Core.Workshop;

/// <summary>
/// Which URL to open for a Workshop item, in order of preference. Pure — the launching
/// is <c>IUriLauncher</c>'s job — so the policy is unit-tested rather than eyeballed.
/// <para>
/// This exists because four separate call sites (Mod Info's "Workshop ↗", the row
/// context menu, the Updates panel and the dependency resolver) each reached straight
/// for <see cref="SteamUrls.WebFilePage"/>, so every one of them opened a browser even
/// with Steam running in the background. One policy, one place.
/// </para>
/// </summary>
public static class SteamLink
{
    /// <summary>
    /// The URLs to try, in order, until one opens.
    /// </summary>
    /// <param name="preferSteamClient">
    /// True when the <c>steam://</c> hand-off is worth making. For <b>viewing</b> a page
    /// that means the client is already <i>running</i> — not merely installed. Launching
    /// a cold Steam client and waiting half a minute to read one description is worse
    /// than the browser tab that opens instantly, and the page is the same page.
    /// For <b>subscribing</b> it is always true, because only the client can do it.
    /// </param>
    public static ImmutableArray<string> Attempts(string publishedFileId, bool preferSteamClient) =>
        preferSteamClient
            ? [SteamUrls.CommunityFilePage(publishedFileId), SteamUrls.WebFilePage(publishedFileId)]
            : [SteamUrls.WebFilePage(publishedFileId)];
}
