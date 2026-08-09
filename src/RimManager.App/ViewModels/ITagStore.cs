using System.Collections.Generic;
using System.Threading.Tasks;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// What Settings ▸ Tags &amp; metadata needs from the rest of the app.
/// <para>
/// A seam rather than a direct reference to the repository, for the reason the whole
/// Settings window works this way: the page edits the <b>live</b> tag set the main window
/// is already showing, so a rename or a recolour reaches the mod lists immediately
/// instead of waiting for a reload. Two copies of the tag set is how the stripe colour
/// and the tag table would come to disagree.
/// </para>
/// </summary>
public interface ITagStore
{
    /// <summary>The live tag set.</summary>
    TagSet Tags { get; }

    /// <summary>Persists and republishes it — the main window re-applies stripes from here.</summary>
    Task SaveAsync(TagSet tags);

    /// <summary>How many mods carry each tag, keyed by tag id.</summary>
    IReadOnlyDictionary<string, int> CountsByTagId();

    /// <summary>How many mods carry at least one tag.</summary>
    int TaggedModCount();

    /// <summary>The METADATA STORAGE line: where the file is, and how big.</summary>
    string StorageLine();

    /// <summary>
    /// Removes a tag AND its assignments. Both, because a tag id left behind on a
    /// mod is a reference to something that no longer exists — invisible until the
    /// next tag happens to reuse the id, and then wrong.
    /// </summary>
    /// <returns>How many mods carried it.</returns>
    Task<int> DeleteTagAsync(string tagId);
}
