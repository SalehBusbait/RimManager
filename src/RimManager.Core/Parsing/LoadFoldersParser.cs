using System.Collections.Immutable;
using System.Xml.Linq;

namespace RimManager.Core.Parsing;

/// <summary>
/// Parsed <c>LoadFolders.xml</c>: the authoritative map of which folders a mod
/// loads for each game version. ~38% of real mods ship one, so content detection
/// and (later) content resolution must honour it rather than guess from
/// version-named subfolders.
/// </summary>
/// <remarks>
/// Format: <c>&lt;loadFolders&gt;&lt;v1.6&gt;&lt;li&gt;/&lt;/li&gt;&lt;li&gt;Current&lt;/li&gt;&lt;/v1.6&gt;...</c>.
/// Each version element lists relative folders in load order; <c>/</c> (or <c>.</c>)
/// means the mod root.
/// </remarks>
public sealed class LoadFolders
{
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _byVersion;

    private LoadFolders(ImmutableDictionary<string, ImmutableArray<string>> byVersion) => _byVersion = byVersion;

    public IEnumerable<string> Versions => _byVersion.Keys;

    /// <summary>Folders for <paramref name="majorMinor"/> (e.g. <c>1.6</c>), or empty if unspecified.</summary>
    public ImmutableArray<string> FoldersFor(string majorMinor) =>
        _byVersion.TryGetValue(Normalize(majorMinor), out var folders) ? folders : [];

    /// <summary>Every distinct folder across all versions (used when the active version is unknown).</summary>
    public ImmutableArray<string> AllFolders() =>
        [.. _byVersion.Values.SelectMany(f => f).Distinct(StringComparer.OrdinalIgnoreCase)];

    public bool HasVersion(string majorMinor) => _byVersion.ContainsKey(Normalize(majorMinor));

    private static string Normalize(string version) =>
        version.TrimStart('v', 'V').Trim().ToLowerInvariant();

    public static LoadFolders Parse(string xml)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return new LoadFolders(builder.ToImmutable());
        }

        var root = doc.Root;
        if (root is not null)
        {
            foreach (var versionElement in root.Elements())
            {
                var version = Normalize(versionElement.Name.LocalName);
                var folders = ImmutableArray.CreateBuilder<string>();
                foreach (var li in versionElement.Elements().Where(e =>
                             string.Equals(e.Name.LocalName, "li", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = li.Value.Trim();
                    if (value.Length > 0) folders.Add(value);
                }

                if (folders.Count > 0) builder[version] = folders.ToImmutable();
            }
        }

        return new LoadFolders(builder.ToImmutable());
    }
}
