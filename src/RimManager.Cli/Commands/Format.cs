using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.Cli.Commands;

/// <summary>Shared compact formatting for the CLI tables.</summary>
internal static class Format
{
    public static string SourceTag(ModSource s) => s switch
    {
        ModSource.Core => "core",
        ModSource.Dlc => "dlc ",
        ModSource.Workshop => "ws  ",
        ModSource.Local => "loc ",
        ModSource.Git => "git ",
        _ => "??? ",
    };

    /// <summary>Compact fixed-width content indicator: C#, Defs, Patches, Textures,
    /// Sounds, Languages — or the whole-row mark for a mod-list item (NF-10), because
    /// its content flags are the ABSENCE of content and six dots say nothing.</summary>
    public static string Flags(Mod m)
    {
        if (m.IsRwListItem) return "=list=";

        Span<char> buf =
        [
            m.HasAssemblies ? 'C' : '·',
            m.Content.HasFlag(ContentFlags.Defs) ? 'D' : '·',
            m.Content.HasFlag(ContentFlags.Patches) ? 'P' : '·',
            m.Content.HasFlag(ContentFlags.Textures) ? 'T' : '·',
            m.Content.HasFlag(ContentFlags.Sounds) ? 'S' : '·',
            m.Content.HasFlag(ContentFlags.Languages) ? 'L' : '·',
        ];
        return new string(buf);
    }

    public static string TierTag(Tier tier) => tier switch
    {
        Tier.Top => "top",
        Tier.PreCore => "precore",
        Tier.Core => "core",
        Tier.Dlc => "dlc",
        Tier.Normal => "normal",
        Tier.Bottom => "bottom",
        _ => "?",
    };

    public static string RuleText(RuleProvenance p)
    {
        var text = $"{p.Source}/{p.Type}";
        if (p.DeclaredBy is { } by) text += $" (from {by.Display})";
        if (!string.IsNullOrWhiteSpace(p.Comment)) text += $" — {p.Comment}";
        return text;
    }
}
