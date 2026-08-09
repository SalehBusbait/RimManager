using System.Text;
using RimManager.Core.Abstractions;

namespace RimManager.Core.Sharing;

/// <summary>
/// Writes the Workshop-item-as-mod-list folder shape (NF-10, slice 3 — the authoring
/// companion): a mod folder whose payload is a <c>.rwlist</c> and whose only other
/// content is an <c>About/About.xml</c>. The About matters because every subscribed
/// item appears in RimWorld's own mod list — with one, the item is a harmless no-op
/// whose description says what it is; without one it renders there as a broken entry.
/// <para>
/// Uploading stays the user's act (RimWorld's dev-mode uploader or any Workshop
/// tool); this writer only produces the folder. The About claims the current game
/// version (T7 decision 6) so subscribers see no amber version warning in-game.
/// </para>
/// </summary>
public static class WorkshopItemFolder
{
    /// <summary>Writes <c>&lt;parentDir&gt;/&lt;slug&gt;/</c> and returns the folder path.
    /// An existing folder of that name gets a numbered sibling, never an overwrite.</summary>
    public static async Task<string> WriteAsync(
        IFileSystem fs, string parentDir, RwList list, string rwlistJson,
        string? gameMajorMinor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(list);

        var name = string.IsNullOrWhiteSpace(list.Name) ? "Mod list" : list.Name.Trim();
        var slug = Slug(name);

        var folder = Path.Combine(parentDir, slug);
        for (var n = 2; fs.DirectoryExists(folder); n++)
            folder = Path.Combine(parentDir, $"{slug}-{n}");

        fs.CreateDirectory(Path.Combine(folder, "About"));

        var mods = list.Mods.Count();
        var author = string.IsNullOrWhiteSpace(list.Author) ? "a RimManager user" : list.Author!.Trim();
        var versions = string.IsNullOrWhiteSpace(gameMajorMinor)
            ? ""
            : $"\n  <supportedVersions>\n    <li>{gameMajorMinor}</li>\n  </supportedVersions>";

        // RimWorld reads this leniently; the description is the one channel that
        // reaches a subscriber inside the game's own mod list.
        var about = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <ModMetaData>
              <name>{Escape(name)} [mod list]</name>
              <author>{Escape(author)}</author>
              <packageId>rimmanager.list.{PackageIdSlug(name)}</packageId>
              <description>A RimManager mod list ({mods} mods), not a mod. Import it with RimManager to get the full load order — activating it here does nothing, and nothing is added to your game by subscribing.</description>{versions}
            </ModMetaData>
            """.ReplaceLineEndings("\n");

        await fs.AtomicWriteAsync(Path.Combine(folder, "About", "About.xml"),
            Encoding.UTF8.GetBytes(about), backup: false, ct).ConfigureAwait(false);
        await fs.AtomicWriteAsync(Path.Combine(folder, $"{slug}.rwlist"),
            Encoding.UTF8.GetBytes(rwlistJson), backup: false, ct).ConfigureAwait(false);

        return folder;
    }

    /// <summary>Folder/file-safe: letters, digits and dashes, never empty.</summary>
    private static string Slug(string name)
    {
        var chars = name.Select(c =>
                char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c)
                : c is ' ' or '-' or '_' or '·' ? '-'
                : '\0')
            .Where(c => c != '\0')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Length > 0 ? slug : "modlist";
    }

    /// <summary>packageId-safe: RimWorld allows letters, digits and dots only.</summary>
    private static string PackageIdSlug(string name)
    {
        var chars = name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return chars.Length > 0 ? new string(chars) : "modlist";
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
