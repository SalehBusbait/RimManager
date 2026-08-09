namespace RimManager.App.ViewModels;

/// <summary>
/// What the three Integrations cards (<c>2g</c>) actually know. Every field is
/// measured, never guessed — which is why two of them are nullable: "we did not find
/// git" and "Steam has never written a manifest here" are real states that must not
/// render as a number.
/// </summary>
/// <param name="SteamClientRunning">A local process probe. Not a session — see
/// <c>SteamClientDetector</c> for why the card says <i>running</i>, not <i>connected</i>.</param>
/// <param name="InstalledWorkshopItems">
/// Items Steam records in <c>appworkshop_294100.acf</c>. Null when there is no manifest
/// (no Steam install, or a Workshop folder we were not pointed at).
/// <para>
/// This is <b>installed</b>, not <b>subscribed</b>. Subscriptions live in the account and
/// need a logged-in Web API call we deliberately do not make; a subscription with nothing
/// downloaded would be invisible here. The card says which one it means.
/// </para>
/// </param>
/// <param name="SteamCmdBytes">Size of RimManager's private SteamCMD, once provisioned.</param>
/// <param name="GitVersion">Null when git is not on PATH — a normal state, not a failure.</param>
/// <param name="GitTrackedRepos">Installed mods that are git working trees.</param>
/// <param name="GitDirtyRepos">Of those, how many have uncommitted changes.</param>
public sealed record IntegrationStatus(
    bool SteamClientRunning,
    int? InstalledWorkshopItems,
    bool SteamCmdInstalled,
    string SteamCmdDir,
    long SteamCmdBytes,
    string? GitVersion,
    string? GitPath,
    int GitTrackedRepos,
    int GitDirtyRepos)
{
    /// <summary>What the page shows before the probe has answered.</summary>
    public static readonly IntegrationStatus Unknown =
        new(false, null, false, string.Empty, 0, null, null, 0, 0);
}

/// <summary>
/// The wording of the Integrations cards. Pure, so the honesty of each line is
/// testable — the failure this guards against is a card that reads as a fact
/// ("342 subscribed items") when the number underneath means something else.
/// </summary>
public static class IntegrationsPresenter
{
    // --- Steam --------------------------------------------------------------

    /// <summary>"Steam client running · 342 Workshop items installed".</summary>
    public static string SteamHeadline(IntegrationStatus s)
    {
        var client = s.SteamClientRunning ? "Steam client running" : "Steam client not running";

        return s.InstalledWorkshopItems switch
        {
            null => $"{client} · no Workshop manifest found",
            0 => $"{client} · no Workshop items installed",
            1 => $"{client} · 1 Workshop item installed",
            var n => $"{client} · {n} Workshop items installed",
        };
    }

    /// <summary>
    /// The status pill. "running" rather than the mockup's "connected": RimManager holds
    /// no Steam session, and a pill claiming a connection we do not have is the kind of
    /// invented fact the rest of this page avoids.
    /// </summary>
    public static string SteamPill(IntegrationStatus s) => s.SteamClientRunning ? "running" : "not running";

    /// <summary>
    /// What Steam is used for — the real list, which is shorter than the mockup's.
    /// Unsubscribing needs the Steamworks SDK against RimWorld's AppID; we hand the
    /// client a URL instead.
    /// </summary>
    public const string SteamUses =
        "Used for: opening a mod's Workshop page, and handing a collection to Steam to subscribe.";

    // --- SteamCMD -----------------------------------------------------------

    /// <summary>
    /// "installed · 271 MB at …" or "not installed · ~250 MB after its own first-run
    /// self-update, installs to …". The mockup says ~180 MB; the figure we have measured
    /// is SteamCMD's self-update, which is the download that actually costs the wait.
    /// </summary>
    /// <param name="pathBudget">
    /// Characters the path may occupy before it is elided in the middle; 0 leaves it
    /// whole, which is what the tooltip wants. Shortening happens <b>here</b> rather than
    /// by letting the control trim, because a TextBlock that trims does not wrap, and a
    /// non-wrapping TextBlock measures at its full natural width — which pushed the
    /// card's buttons off the edge of the window.
    /// </param>
    public static string SteamCmdDetail(IntegrationStatus s, int pathBudget = 0)
    {
        var dir = ShortenPath(s.SteamCmdDir, pathBudget);

        // Middot-separated clauses at 2g's density. The earlier sentence form ran to
        // ~110 characters once a Windows path was in it, which wraps a card's detail
        // line to three rows on a narrow window.
        return s.SteamCmdInstalled
            ? $"installed · {UpdatesPresenter.Size(s.SteamCmdBytes)} · {dir}"
            : $"not installed · ~250 MB on first run · installs to {dir}";
    }

    /// <summary>
    /// Elides the MIDDLE of a path, keeping the root and the last segments — the two ends
    /// that identify it. Trimming the tail would leave every Windows path reading
    /// <c>C:\Users\PC\AppData\Local\…</c>, which is the half that is the same for everyone.
    /// </summary>
    public static string ShortenPath(string? path, int budget)
    {
        if (string.IsNullOrEmpty(path) || budget <= 0 || path.Length <= budget) return path ?? string.Empty;

        var separator = path.Contains('\\') ? '\\' : '/';
        var parts = path.Split(separator);

        if (parts.Length >= 3)
        {
            // Grow the tail one segment at a time while the whole thing still fits, so a
            // deep path keeps as much of its distinctive end as the budget allows.
            var head = parts[0] + separator + "…";
            var tail = string.Empty;

            for (var i = parts.Length - 1; i >= 1; i--)
            {
                var candidate = separator + parts[i] + tail;
                if (head.Length + candidate.Length > budget) break;
                tail = candidate;
            }

            if (tail.Length > 0) return head + tail;
        }

        // Too few segments to split, or one segment longer than the whole budget. Show
        // the END rather than nothing: that is the part that differs between paths.
        return "…" + path[^Math.Min(path.Length, Math.Max(budget - 1, 0))..];
    }

    public const string SteamCmdUses = "SteamCMD — anonymous downloads without a subscription";

    // --- git ----------------------------------------------------------------

    /// <summary>"git 2.45.2 · 2 tracked mods · 1 with uncommitted changes".</summary>
    public static string GitHeadline(IntegrationStatus s)
    {
        if (s.GitVersion is null) return "git not found on PATH";

        var tracked = s.GitTrackedRepos switch
        {
            0 => "no tracked mods",
            1 => "1 tracked mod",
            var n => $"{n} tracked mods",
        };

        // Only stated when there is something to state — a permanent "0 with
        // uncommitted changes" is noise on the 99% of installs that track nothing.
        var dirty = s.GitDirtyRepos > 0 ? $" · {s.GitDirtyRepos} with uncommitted changes" : string.Empty;

        return $"git {s.GitVersion} · {tracked}{dirty}";
    }

    /// <summary>The card's second line: where git is, or why there is nothing to show.</summary>
    /// <param name="pathBudget">As <see cref="SteamCmdDetail"/>; 0 leaves the path whole.</param>
    public static string GitPathLine(IntegrationStatus s, int pathBudget = 0) =>
        s.GitPath is null
            ? "install git to track a mod you are developing from a clone"
            : ShortenPath(s.GitPath, pathBudget);

    // CanManageRepos is GONE (UI audit): it computed an enable state for a button
    // that is deliberately disabled until a repo editor exists — nothing bound it.
}
