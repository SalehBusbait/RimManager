using System.Collections.Immutable;

namespace RimManager.Core.Github;

/// <summary>A downloadable file attached to a GitHub release.</summary>
public sealed record GitHubReleaseAsset
{
    public required string Name { get; init; }

    /// <summary>Direct download URL (<c>browser_download_url</c>).</summary>
    public required string DownloadUrl { get; init; }

    public long Size { get; init; }

    public string? ContentType { get; init; }
}

/// <summary>
/// A GitHub release, as returned by the public <c>/repos/{owner}/{repo}/releases</c>
/// API. Used to update-check and fetch mods distributed off the Workshop (Git-sourced
/// mods, or Workshop mods whose <c>About.xml</c> <c>url</c> points at a GitHub repo).
/// </summary>
public sealed record GitHubRelease
{
    /// <summary>The git tag, e.g. <c>v1.4.0</c> — the closest thing to a mod version here.</summary>
    public required string TagName { get; init; }

    /// <summary>Human release title; may be empty, in which case the tag is the label.</summary>
    public string? Name { get; init; }

    public DateTimeOffset? PublishedAtUtc { get; init; }

    public bool IsPrerelease { get; init; }

    public bool IsDraft { get; init; }

    /// <summary>The release's web page (<c>html_url</c>).</summary>
    public string? HtmlUrl { get; init; }

    public ImmutableArray<GitHubReleaseAsset> Assets { get; init; } = [];

    /// <summary>Best label: the release name if it has one, else the tag.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? TagName : Name!;
}
