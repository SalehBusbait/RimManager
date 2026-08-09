namespace RimManager.App.ViewModels;

/// <summary>
/// One byte formatter for the whole UI, so the image viewer's footer and Mod Info's
/// size line cannot disagree about what "1.5 MB" means.
/// </summary>
public static class ByteSize
{
    /// <summary>
    /// "912 KB", "245.3 MB", "1.2 GB". Binary units, because that is what a file
    /// manager on either platform reports and a number the user can cross-check is
    /// worth more than a strictly-SI one they cannot.
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:0.#} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:0.#} MB";
        // Rounded to whole KB, but never to "0 KB": a file that exists has a size,
        // and rounding it away reads as a failure to measure.
        return $"{Math.Max(1, (long)Math.Round(bytes / 1024.0))} KB";
    }
}
