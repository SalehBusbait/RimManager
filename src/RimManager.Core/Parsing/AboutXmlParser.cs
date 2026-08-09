using System.Collections.Immutable;
using System.Xml.Linq;
using RimManager.Core.Domain;

namespace RimManager.Core.Parsing;

/// <summary>
/// Parses a mod's <c>About/About.xml</c> into <see cref="AboutMetadata"/>. Pure:
/// text in, data out, never throws for malformed content — problems become
/// <see cref="ModWarning"/>s (domain primer §3: "never crash; surface it").
/// </summary>
public static class AboutXmlParser
{
    /// <summary>
    /// Parses <paramref name="xml"/>. On a hard XML syntax error, returns metadata
    /// with only a single error warning so the scanner can still show the mod.
    /// </summary>
    public static AboutMetadata Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            return new AboutMetadata
            {
                Warnings = [new ModWarning(WarningSeverity.Error, "about.invalid-xml",
                    $"About.xml is not valid XML: {ex.Message}")],
            };
        }

        var root = doc.Root;
        if (root is null)
        {
            return new AboutMetadata
            {
                Warnings = [new ModWarning(WarningSeverity.Error, "about.empty", "About.xml has no root element.")],
            };
        }

        var warnings = ImmutableArray.CreateBuilder<ModWarning>();

        var packageId = Trimmed(Child(root, "packageId")?.Value);
        if (string.IsNullOrWhiteSpace(packageId))
        {
            warnings.Add(new ModWarning(WarningSeverity.Error, "about.missing-packageId",
                "About.xml has no <packageId>."));
            packageId = null;
        }

        var name = Trimmed(Child(root, "name")?.Value);
        if (string.IsNullOrWhiteSpace(name))
        {
            warnings.Add(new ModWarning(WarningSeverity.Warning, "about.missing-name",
                "About.xml has no <name>."));
        }

        return new AboutMetadata
        {
            PackageId = packageId,
            Name = name,
            Authors = ParseAuthors(root),
            Description = Trimmed(Child(root, "description")?.Value),
            SupportedVersions = ListItems(root, "supportedVersions"),
            ModVersion = Trimmed(Child(root, "modVersion")?.Value),
            Dependencies = ParseDependencies(root),
            LoadAfter = ListItems(root, "loadAfter"),
            LoadBefore = ListItems(root, "loadBefore"),
            ForceLoadAfter = ListItems(root, "forceLoadAfter"),
            ForceLoadBefore = ListItems(root, "forceLoadBefore"),
            IncompatibleWith = ListItems(root, "incompatibleWith"),
            Warnings = warnings.ToImmutable(),
        };
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>Case-insensitive first child by local name (modders vary the casing).</summary>
    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static string? Trimmed(string? value)
    {
        if (value is null) return null;
        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>The <c>&lt;li&gt;</c> string values of a list element, skipping blanks.</summary>
    private static ImmutableArray<string> ListItems(XElement root, string listName)
    {
        var list = Child(root, listName);
        if (list is null) return [];

        var items = ImmutableArray.CreateBuilder<string>();
        foreach (var li in list.Elements().Where(e =>
                     string.Equals(e.Name.LocalName, "li", StringComparison.OrdinalIgnoreCase)))
        {
            var v = Trimmed(li.Value);
            if (v is not null) items.Add(v);
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<string> ParseAuthors(XElement root)
    {
        // Preferred: <authors><li>..</li></authors>. Fallback: <author> possibly
        // comma-separated ("OskarPotocki, Atlas, Kikohi").
        var authorsList = ListItems(root, "authors");
        if (authorsList.Length > 0) return authorsList;

        var single = Trimmed(Child(root, "author")?.Value);
        if (single is null) return [];

        return [.. single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static ImmutableArray<ModDependency> ParseDependencies(XElement root)
    {
        var list = Child(root, "modDependencies");
        if (list is null) return [];

        var deps = ImmutableArray.CreateBuilder<ModDependency>();
        foreach (var li in list.Elements().Where(e =>
                     string.Equals(e.Name.LocalName, "li", StringComparison.OrdinalIgnoreCase)))
        {
            var pid = Trimmed(Child(li, "packageId")?.Value);
            if (pid is null) continue; // a dependency with no packageId is meaningless

            deps.Add(new ModDependency(
                ModId.From(pid),
                DisplayName: Trimmed(Child(li, "displayName")?.Value),
                SteamWorkshopUrl: Trimmed(Child(li, "steamWorkshopUrl")?.Value),
                DownloadUrl: Trimmed(Child(li, "downloadUrl")?.Value)));
        }

        return deps.ToImmutable();
    }
}
