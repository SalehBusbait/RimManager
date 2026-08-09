using System.Collections.Immutable;

namespace RimManager.Core.Github;

/// <summary>What the update check concluded: the release worth offering, and how.</summary>
public sealed record AppUpdateAdvice(
    GitHubRelease Release,
    string Version,
    GitHubReleaseAsset? Installer)
{
    /// <summary>The release page, for platforms with no installer to run.</summary>
    public string? PageUrl => Release.HtmlUrl;
}

/// <summary>
/// Decides whether a newer RimManager exists, from the app's own version and the
/// repository's releases. Pure — the caller fetches, this judges.
/// </summary>
/// <remarks>
/// Channel rule: pre-releases are offered only while the running version is itself a
/// pre-release. Someone on <c>1.0.0-beta.2</c> should hear about <c>beta.3</c>;
/// someone on <c>1.0.0</c> should not be offered a beta.
/// </remarks>
public static class AppUpdateAdvisor
{
    /// <summary>
    /// The newest applicable release strictly newer than <paramref name="currentVersion"/>,
    /// or null when up to date (or nothing parses). Build metadata after <c>+</c> is
    /// ignored on both sides, matching SemVer.
    /// </summary>
    public static AppUpdateAdvice? Advise(
        string? currentVersion, IEnumerable<GitHubRelease> releases)
    {
        if (string.IsNullOrWhiteSpace(currentVersion)) return null;

        var current = SemVer.Parse(currentVersion);
        if (current is null) return null;

        var allowPrerelease = current.Value.Prerelease.Length > 0;

        AppUpdateAdvice? best = null;
        SemVer.Version bestVersion = default;

        foreach (var release in releases)
        {
            if (release.IsDraft) continue;
            if (release.IsPrerelease && !allowPrerelease) continue;

            var tag = release.TagName.TrimStart('v', 'V');
            var candidate = SemVer.Parse(tag);
            if (candidate is null) continue;
            if (SemVer.Compare(candidate.Value, current.Value) <= 0) continue;
            if (best is not null && SemVer.Compare(candidate.Value, bestVersion) <= 0) continue;

            best = new AppUpdateAdvice(release, tag, FindInstaller(release));
            bestVersion = candidate.Value;
        }

        return best;
    }

    /// <summary>The Windows Setup asset, by the release workflow's naming.</summary>
    private static GitHubReleaseAsset? FindInstaller(GitHubRelease release) =>
        release.Assets.FirstOrDefault(a =>
            a.Name.StartsWith("RimManager-Setup-", StringComparison.OrdinalIgnoreCase)
            && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The slice of SemVer this app actually publishes: <c>major.minor.patch</c> with an
    /// optional dot-separated pre-release. Comparison follows the spec — numeric
    /// identifiers compare numerically, a pre-release sorts before its release, and a
    /// longer pre-release wins a shared prefix (<c>beta.2</c> &lt; <c>beta.2.1</c>).
    /// </summary>
    internal static class SemVer
    {
        public readonly record struct Version(int Major, int Minor, int Patch, string[] Prerelease);

        public static Version? Parse(string text)
        {
            var plus = text.IndexOf('+');
            if (plus >= 0) text = text[..plus];

            var dash = text.IndexOf('-');
            var pre = dash >= 0 ? text[(dash + 1)..].Split('.') : [];
            var core = dash >= 0 ? text[..dash] : text;

            var parts = core.Split('.');
            if (parts.Length != 3) return null;
            if (!int.TryParse(parts[0], out var major)) return null;
            if (!int.TryParse(parts[1], out var minor)) return null;
            if (!int.TryParse(parts[2], out var patch)) return null;

            return new Version(major, minor, patch, pre);
        }

        public static int Compare(Version a, Version b)
        {
            if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
            if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
            if (a.Patch != b.Patch) return a.Patch.CompareTo(b.Patch);

            // Equal cores: a release outranks any of its pre-releases.
            if (a.Prerelease.Length == 0 && b.Prerelease.Length == 0) return 0;
            if (a.Prerelease.Length == 0) return 1;
            if (b.Prerelease.Length == 0) return -1;

            for (var i = 0; i < Math.Min(a.Prerelease.Length, b.Prerelease.Length); i++)
            {
                var (x, y) = (a.Prerelease[i], b.Prerelease[i]);
                var xNum = int.TryParse(x, out var xv);
                var yNum = int.TryParse(y, out var yv);

                int c;
                if (xNum && yNum) c = xv.CompareTo(yv);
                else if (xNum) c = -1;          // numeric sorts before alphanumeric
                else if (yNum) c = 1;
                else c = string.CompareOrdinal(x, y);

                if (c != 0) return c;
            }

            return a.Prerelease.Length.CompareTo(b.Prerelease.Length);
        }
    }
}
