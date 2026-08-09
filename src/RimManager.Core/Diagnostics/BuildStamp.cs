using System.Reflection;

namespace RimManager.Core.Diagnostics;

/// <summary>
/// The build's identity, human-shaped (N9): <c>"0.9.0+a0d6b62def…"</c> becomes
/// <c>"0.9.0 (a0d6b62)"</c>.
/// <para>
/// The commit is the identifier that matters. Every pre-release build shares one
/// version number, and there is no release pipeline handing out distinct ones — the
/// repo is the delivery mechanism — so a bug report saying only "0.9.0" names three
/// phases of commits. The seven-character short form matches what
/// <c>git log --oneline</c> prints, so the line in a report reads directly against
/// the history.
/// </para>
/// </summary>
public static class BuildStamp
{
    /// <summary>Short-commit length, matching <c>git log --oneline</c>.</summary>
    private const int ShortSha = 7;

    /// <summary>
    /// <c>"1.2.3+sha"</c> → <c>"1.2.3 (sha7)"</c>; no metadata → the version alone;
    /// nothing → null, so the caller owns the fallback wording.
    /// </summary>
    public static string? Describe(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;

        var plus = informationalVersion.IndexOf('+');
        if (plus <= 0) return informationalVersion;

        var version = informationalVersion[..plus];
        var metadata = informationalVersion[(plus + 1)..];
        if (metadata.Length == 0) return version;

        var sha = metadata.Length > ShortSha ? metadata[..ShortSha] : metadata;
        return $"{version} ({sha})";
    }

    /// <summary>The stamp for an assembly, with the fallback a dev loop deserves —
    /// an assembly with no informational version is a local build, not an error.</summary>
    public static string ForAssembly(Assembly assembly) =>
        Describe(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? "development build";
}
