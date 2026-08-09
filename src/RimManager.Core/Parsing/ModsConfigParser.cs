using System.Collections.Immutable;
using System.Xml.Linq;
using RimManager.Core.Domain;

namespace RimManager.Core.Parsing;

/// <summary>Parses <c>ModsConfig.xml</c> (root <c>ModsConfigData</c>) into a <see cref="ModsConfig"/>.</summary>
public static class ModsConfigParser
{
    public static ModsConfig Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root
            ?? throw new FormatException("ModsConfig.xml has no root element.");

        var version = root.Element("version")?.Value.Trim() ?? string.Empty;
        var active = LiIds(root.Element("activeMods"));
        var known = LiIds(root.Element("knownExpansions"));

        return new ModsConfig(version, active, known);
    }

    private static ImmutableArray<ModId> LiIds(XElement? container)
    {
        if (container is null) return [];

        var ids = ImmutableArray.CreateBuilder<ModId>();
        foreach (var li in container.Elements("li"))
        {
            if (ModId.TryFrom(li.Value.Trim(), out var id))
            {
                ids.Add(id);
            }
        }

        return ids.ToImmutable();
    }
}
