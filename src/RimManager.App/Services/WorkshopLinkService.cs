using System;
using RimManager.Core.Abstractions;
using RimManager.Core.Workshop;

namespace RimManager.App.Services;

/// <summary>
/// Opens a Workshop page, preferring the Steam client and falling back to the browser.
/// The one place in the app that decides which; every "Workshop ↗" goes through it.
/// <para>
/// The fallback is a real walk, not a guess: <c>steam://</c> throws when no handler is
/// registered, and the next URL in the list is tried. What it never does is silently
/// substitute a <i>different act</i> — viewing a page in a browser is the same act as
/// viewing it in the client, which is exactly why this may fall back automatically
/// while subscribe-versus-SteamCMD may not.
/// </para>
/// </summary>
public sealed class WorkshopLinkService(IUriLauncher launcher, Func<bool> steamClientRunning)
{
    /// <summary>
    /// Opens the item for <b>reading</b>. Uses the client only when it is already up —
    /// see <see cref="SteamLink.Attempts"/> for why "installed" is the wrong test.
    /// </summary>
    /// <returns>The URL that opened, or null if none did.</returns>
    public string? Open(string publishedFileId) =>
        Walk(publishedFileId, preferSteamClient: Probe());

    /// <summary>
    /// Opens the item's page in order to <b>act</b> on it — subscribe, or a collection's
    /// "Subscribe to all". Always tries the client first: only the client can subscribe,
    /// so waiting for it to start is the point rather than a cost.
    /// </summary>
    public string? OpenToSubscribe(string publishedFileId) =>
        Walk(publishedFileId, preferSteamClient: true);

    private string? Walk(string publishedFileId, bool preferSteamClient)
    {
        foreach (var url in SteamLink.Attempts(publishedFileId, preferSteamClient))
        {
            try
            {
                launcher.Launch(url);
                return url;
            }
            catch (Exception)
            {
                // No handler for this scheme — fall through to the next candidate.
            }
        }

        return null;
    }

    /// <summary>
    /// Opens a Workshop SEARCH for a term — the Find of last resort, when a missing
    /// dependency declared no URL to follow. Browser URL only: search is a reading
    /// act, and the browse page is the one Workshop surface with no client deep-link.
    /// </summary>
    public string? OpenSearch(string term)
    {
        var url = "https://steamcommunity.com/workshop/browse/?appid=294100&searchtext="
            + Uri.EscapeDataString(term);
        try
        {
            launcher.Launch(url);
            return url;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Probed per click rather than cached: enumerating processes costs a few tens of
    /// milliseconds once, and a cached answer would keep sending someone to the browser
    /// for the rest of the session because Steam happened to be closed at startup.
    /// A failed probe means the browser, which always works.
    /// </summary>
    private bool Probe()
    {
        try { return steamClientRunning(); }
        catch (Exception) { return false; }
    }
}
