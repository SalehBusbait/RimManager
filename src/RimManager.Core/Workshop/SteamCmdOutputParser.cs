using System.Collections.Immutable;
using System.Text.RegularExpressions;
using RimManager.Core.Domain;

namespace RimManager.Core.Workshop;

/// <summary>
/// Extracts per-item download outcomes from SteamCMD's console output. Pure and
/// tolerant: SteamCMD interleaves its self-update chatter and prints the result line
/// without a trailing newline, so the patterns are matched anywhere in the text, not
/// line-anchored. Verified against real output captured on a live anonymous download.
/// </summary>
/// <remarks>
/// The two shapes SteamCMD emits per item:
/// <code>
/// Success. Downloaded item 3346964576 to "…\content\294100\3346964576" (89783 bytes)
/// ERROR! Download item 3346964576 failed (Failure).
/// </code>
/// </remarks>
public static partial class SteamCmdOutputParser
{
    [GeneratedRegex("""Success\.\s*Downloaded item\s+(?<id>\d+)\s+to\s+"(?<path>[^"]*)"\s*\((?<bytes>\d+)\s*bytes\)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex SuccessPattern();

    [GeneratedRegex(@"ERROR!\s*Download item\s+(?<id>\d+)\s+failed\s*\((?<reason>[^)]*)\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex FailurePattern();

    /// <summary>
    /// Parses all download outcomes in <paramref name="output"/>. If an id appears as
    /// both success and failure (SteamCMD retries), the success wins.
    /// </summary>
    public static ImmutableArray<WorkshopDownloadResult> Parse(string output)
    {
        if (string.IsNullOrEmpty(output)) return [];

        // Keyed so a later success overrides an earlier failure for the same id.
        var byId = new Dictionary<string, WorkshopDownloadResult>(StringComparer.Ordinal);

        foreach (Match m in FailurePattern().Matches(output))
        {
            var id = m.Groups["id"].Value;
            byId[id] = new WorkshopDownloadResult
            {
                PublishedFileId = id,
                Success = false,
                Error = m.Groups["reason"].Value.Trim() is { Length: > 0 } r ? r : "unknown",
            };
        }

        foreach (Match m in SuccessPattern().Matches(output))
        {
            var id = m.Groups["id"].Value;
            byId[id] = new WorkshopDownloadResult
            {
                PublishedFileId = id,
                Success = true,
                DownloadedPath = m.Groups["path"].Value,
                Bytes = long.TryParse(m.Groups["bytes"].Value, out var b) ? b : null,
            };
        }

        return [.. byId.Values];
    }

    /// <summary>Convenience: the outcome for a specific id, or null if absent from the output.</summary>
    public static WorkshopDownloadResult? ResultFor(string output, string publishedFileId) =>
        Parse(output).FirstOrDefault(r => r.PublishedFileId == publishedFileId);
}
