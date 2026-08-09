using System.Diagnostics.CodeAnalysis;

namespace RimManager.Core.Github;

/// <summary>
/// An <c>owner/repo</c> pair. Parses the things a mod's <c>About.xml</c> <c>url</c>
/// (or a user) might supply: a full GitHub URL or a bare <c>owner/repo</c>. Pure
/// string work — no <c>Uri</c> dependency — so it behaves identically everywhere.
/// </summary>
public sealed record GitHubRepoRef(string Owner, string Repo)
{
    public override string ToString() => $"{Owner}/{Repo}";

    /// <summary>
    /// Accepts <c>owner/repo</c>, <c>https://github.com/owner/repo</c>,
    /// <c>github.com/owner/repo/…</c> (extra path/query/fragment ignored), and a
    /// trailing <c>.git</c>. Rejects anything without both segments.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out GitHubRepoRef? repo)
    {
        repo = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();

        // Strip scheme.
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];

        // Strip a leading github.com host (with or without it, we look for owner/repo).
        if (s.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            s = s["github.com/".Length..];
        }
        else if (s.Contains('.') && s.IndexOf('/') > s.IndexOf('.'))
        {
            // Some other host — only GitHub is supported here.
            return false;
        }

        // Drop query/fragment, then split into path segments.
        s = s.Split('?', '#')[0].Trim('/');
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        var owner = parts[0];
        var name = parts[1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (owner.Length == 0 || name.Length == 0) return false;

        repo = new GitHubRepoRef(owner, name);
        return true;
    }
}
