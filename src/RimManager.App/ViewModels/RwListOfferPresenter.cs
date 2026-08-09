using System.Collections.Generic;
using System.Linq;
using RimManager.Core.Domain;
using RimManager.Core.Sharing;

namespace RimManager.App.ViewModels;

/// <summary>A Workshop list item awaiting its one import offer.</summary>
/// <param name="SeenKey">Workshop item id, else packageId — what the seen-set stores.</param>
public sealed record RwListOffer(ModId PackageId, string SeenKey, string ModName, string RootPath);

/// <summary>
/// The offer's Avalonia-free decisions (NF-10, S-RWLIST): which item is offered next
/// and what the strip says. One offer at a time, sequentially — several new list items
/// in one scan is rare enough that a queue of strips would be machinery without a user.
/// </summary>
public static class RwListOfferPresenter
{
    /// <summary>
    /// The next unseen list item, or null. <b>Workshop-source only</b> (T7 decision 2):
    /// a local list item is almost always the user's own export, and offering to import
    /// what they just exported is noise. Name-ordered so the sequence is stable across
    /// rescans.
    /// </summary>
    public static RwListOffer? NextUnseen(IEnumerable<Mod> mods, RwListOfferSeen seen)
    {
        return mods
            .Where(m => m.IsRwListItem && m.Source == ModSource.Workshop)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => new RwListOffer(m.PackageId, SeenKeyFor(m), m.Name, m.RootPath))
            .FirstOrDefault(o => !seen.Contains(o.SeenKey));
    }

    public static string SeenKeyFor(Mod mod) => mod.PublishedFileId ?? mod.PackageId.Value;

    public static string StripHeadline(RwListOffer offer) =>
        $"Workshop item “{offer.ModName}” looks like a mod list";

    public const string StripDetail =
        "Import it as a modlist, or dismiss — the item stays in your inactive pane either way.";
}
