namespace RimManager.Core.Workshop;

/// <summary>
/// Builds the Steam URLs RimManager hands off to the environment. Pure string work —
/// no launching here (that's <c>IUriLauncher</c>) — so the exact strings are unit-tested.
/// </summary>
/// <remarks>
/// The <c>steam://</c> forms target the user's <em>already-running, logged-in</em>
/// desktop client, which owns the game — so opening a collection's page lets Steam's
/// native "Subscribe to all" download with the active account, no separate login and
/// no Steamworks-SDK/ToS entanglement. The <c>https://</c> forms are the browser
/// fallback when the protocol handler isn't registered.
/// </remarks>
public static class SteamUrls
{
    /// <summary>Deep-link that opens an item or collection page in the Steam client
    /// (a collection page carries "Subscribe to all").</summary>
    public static string CommunityFilePage(string publishedFileId) =>
        $"steam://url/CommunityFilePage/{Require(publishedFileId)}";

    /// <summary>The browser URL for the same item/collection (fallback when steam:// can't open).</summary>
    public static string WebFilePage(string publishedFileId) =>
        $"https://steamcommunity.com/sharedfiles/filedetails/?id={Require(publishedFileId)}";

    /// <summary>
    /// A published-file id is a run of digits; reject anything else so a caller can
    /// never smuggle extra URL/path/query text into a launched deep-link.
    /// </summary>
    private static string Require(string publishedFileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);
        foreach (var c in publishedFileId)
        {
            if (c is < '0' or > '9')
            {
                throw new ArgumentException($"Not a numeric published-file id: '{publishedFileId}'.", nameof(publishedFileId));
            }
        }

        return publishedFileId;
    }
}
