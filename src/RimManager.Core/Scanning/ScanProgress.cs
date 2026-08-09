namespace RimManager.Core.Scanning;

/// <summary>
/// How far a scan has got, for the first-scan window state (<c>2k</c>):
/// <c>218 / 342 · workshop/content/294100</c>.
/// </summary>
/// <param name="Done">Folders examined so far.</param>
/// <param name="Total">
/// Folders that will be examined in total. Known before the first folder is read
/// because every root is enumerated up front — a progress bar whose total grows as
/// it runs is worse than no bar at all.
/// </param>
/// <param name="Root">
/// The source root currently being read. Deliberately the ROOT and not the mod
/// folder: at ~30 folders a millisecond a per-folder path is an unreadable blur,
/// and the granularity at which a scan is actually slow is the root — a cold
/// Workshop directory, a network drive.
/// </param>
public readonly record struct ScanProgress(int Done, int Total, string Root)
{
    /// <summary>0..1, or 0 when there is nothing to scan (never a divide by zero).</summary>
    public double Fraction => Total <= 0 ? 0 : (double)Done / Total;

    /// <summary>
    /// The tail of <see cref="Root"/> — at most <paramref name="segments"/> path
    /// segments, forward-slashed, so the line reads the same on every platform and
    /// does not carry a drive letter and six parent folders nobody is reading.
    /// </summary>
    public string ShortRoot(int segments = 3)
    {
        if (string.IsNullOrEmpty(Root)) return string.Empty;

        var parts = Root.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', parts.Length <= segments ? parts : parts[^segments..]);
    }
}
