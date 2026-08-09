using System.Collections.Immutable;
using RimManager.Core.Parsing;

namespace RimManager.Core.Sharing;

/// <summary>Parses shareable modlists (<c>.rwlist</c> or <c>ModsConfig.xml</c>) into the common model.</summary>
public static class RwListImport
{
    /// <param name="checksumValid">
    /// False only when a <c>.rwlist</c> carries a checksum that doesn't match its content.
    /// </param>
    public static RwList Load(string content, out bool checksumValid)
    {
        if (LooksLikeXml(content))
        {
            checksumValid = true;
            return FromModsConfig(content);
        }

        var list = RwListSerializer.Parse(content);
        checksumValid = RwListSerializer.VerifyChecksum(list);
        return list;
    }

    private static bool LooksLikeXml(string content) =>
        content.TrimStart().StartsWith('<');

    public static RwList FromModsConfig(string xml)
    {
        var config = ModsConfigParser.Parse(xml);
        var entries = config.ActiveMods
            .Select(id => RwEntry.Mod(id.Value, id.Display, RwSource.Workshop))
            .ToImmutableArray();

        return new RwList
        {
            GameVersion = config.MajorMinor,
            RequiredDlc = [.. config.KnownExpansions.Select(x => x.Value)],
            Entries = entries,
        };
    }
}
