using System.Collections.Immutable;
using System.Text;
using RimManager.Core.Domain;
using RimManager.Core.Writing;

namespace RimManager.Core.Sharing;

/// <summary>Renders a <see cref="RwList"/> to the various export targets (spec §4.7).</summary>
public static class RwListExport
{
    /// <summary>Full-fidelity <c>.rwlist</c> JSON (with checksum).</summary>
    public static string ToRwList(RwList list) => RwListSerializer.Serialize(list);

    /// <summary>Drop-in <c>ModsConfig.xml</c> (order + packageIds only).</summary>
    public static string ToModsConfig(RwList list)
    {
        var active = list.Mods
            .Where(e => e.PackageId is not null)
            .Select(e => ModId.From(e.PackageId!))
            .ToImmutableArray();
        var known = list.RequiredDlc.Select(ModId.From).ToImmutableArray();
        return ModsConfigWriter.Serialize(new ModsConfig(list.GameVersion ?? string.Empty, active, known));
    }

    /// <summary>Markdown for forum/Discord posts: separators become headings, mods become Workshop links.</summary>
    public static string ToMarkdown(RwList list)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(list.Name ?? "RimWorld modlist").Append('\n');
        if (!string.IsNullOrWhiteSpace(list.Author)) sb.Append("*by ").Append(list.Author).Append("*\n");
        if (!string.IsNullOrWhiteSpace(list.Description)) sb.Append('\n').Append(list.Description).Append('\n');
        if (!string.IsNullOrWhiteSpace(list.GameVersion)) sb.Append("\nRimWorld ").Append(list.GameVersion).Append('\n');
        sb.Append('\n');

        foreach (var entry in list.Entries)
        {
            if (entry.Type == RwEntryKind.Separator)
            {
                sb.Append("\n## ").Append(entry.Name).Append('\n');
                continue;
            }

            var name = entry.DisplayName ?? entry.PackageId ?? "mod";
            sb.Append("- ");
            if (entry.PublishedFileId is { } id)
                sb.Append('[').Append(name).Append("](https://steamcommunity.com/sharedfiles/filedetails/?id=").Append(id).Append(')');
            else
                sb.Append(name).Append(" (`").Append(entry.PackageId).Append("`)");
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>CSV for spreadsheet people.</summary>
    public static string ToCsv(RwList list)
    {
        var sb = new StringBuilder();
        sb.Append("index,packageId,name,source,version\n");
        int index = 1;
        foreach (var entry in list.Mods)
        {
            sb.Append(index++).Append(',')
              .Append(Csv(entry.PackageId)).Append(',')
              .Append(Csv(entry.DisplayName)).Append(',')
              .Append(Csv(entry.Source.ToString())).Append(',')
              .Append(Csv(entry.ModVersion)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : value;
    }
}
