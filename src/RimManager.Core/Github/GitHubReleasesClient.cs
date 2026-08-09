using System.Collections.Immutable;
using RimManager.Core.Abstractions;

namespace RimManager.Core.Github;

/// <summary>
/// Reads releases from GitHub's public REST API. Pure orchestration over an
/// <see cref="IHttpFetcher"/> and <see cref="GitHubReleaseParser"/> — the GET-side
/// counterpart to <c>SteamWorkshopClient</c>, and unit-testable with a fake fetcher.
/// </summary>
/// <remarks>
/// Uses unauthenticated endpoints (60 requests/hour/IP — ample for update-checks). A
/// repo with no releases returns a 404 from <c>/releases/latest</c>; that's mapped to
/// <c>null</c> rather than surfaced as an error, since "no releases yet" is normal.
/// GitHub rejects requests without a User-Agent — the fetcher always sets one.
/// </remarks>
public sealed class GitHubReleasesClient(IHttpFetcher fetcher)
{
    internal const string ApiBase = "https://api.github.com";

    private readonly IHttpFetcher _fetcher = fetcher;

    /// <summary>The latest published (non-draft, non-prerelease) release, or null if none exists.</summary>
    public async Task<GitHubRelease?> GetLatestReleaseAsync(GitHubRepoRef repo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repo);

        try
        {
            var body = await _fetcher.GetStringAsync(
                $"{ApiBase}/repos/{repo.Owner}/{repo.Repo}/releases/latest", ct).ConfigureAwait(false);
            return GitHubReleaseParser.ParseSingle(body);
        }
        catch (HttpFetchException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Recent releases, newest first, including prereleases (GitHub's default ordering).
    /// Returns empty for a repo with no releases.
    /// </summary>
    public async Task<ImmutableArray<GitHubRelease>> GetReleasesAsync(
        GitHubRepoRef repo, int perPage = 30, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repo);
        var capped = Math.Clamp(perPage, 1, 100);

        try
        {
            var body = await _fetcher.GetStringAsync(
                $"{ApiBase}/repos/{repo.Owner}/{repo.Repo}/releases?per_page={capped}", ct).ConfigureAwait(false);
            return GitHubReleaseParser.Parse(body);
        }
        catch (HttpFetchException ex) when (ex.StatusCode == 404)
        {
            return [];
        }
    }
}
