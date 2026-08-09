using System.Collections.Immutable;
using System.Text.RegularExpressions;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>Stackable filter predicates + search (spec §4.3). Empty criteria match everything.</summary>
public sealed record FilterCriteria
{
    public string? Search { get; init; }
    public bool UseRegex { get; init; }
    public ModSource? Source { get; init; }
    public bool WarningsOnly { get; init; }

    /// <summary>Mod ids that are the subject of a current validation warning (drives the Warnings filter).</summary>
    public ImmutableHashSet<ModId> WarnedIds { get; init; } = [];

    /// <summary>Ticked tag ids from the Tags ▾ flyout. Combined by <see cref="MatchAllTags"/>.</summary>
    public ImmutableHashSet<string> SelectedTagIds { get; init; } = [];

    /// <summary>Match all (every ticked tag must be carried) vs the default Match any.</summary>
    public bool MatchAllTags { get; init; }

    /// <summary>
    /// The Untagged pseudo-tag: a mod "carries" it when it carries no tags at all, so it
    /// combines with the real tags under the same any/all rule — which makes
    /// Match all + Untagged + a real tag provably empty rather than quietly redefined.
    /// </summary>
    public bool UntaggedOnly { get; init; }

    /// <summary>
    /// The Favourites pseudo-tag (O14). Favourite is metadata a mod carries exactly the
    /// way a tag is, so it filters as one rather than as a fourth chip — which is what
    /// lets one control cover all of a mod's metadata, the same argument Untagged is
    /// here on.
    /// </summary>
    public bool FavouritesOnly { get; init; }

    /// <summary>Tag ids per mod, from mod metadata — the data <see cref="SelectedTagIds"/> filters against.</summary>
    public ImmutableDictionary<ModId, ImmutableHashSet<string>> TagsByMod { get; init; } =
        ImmutableDictionary<ModId, ImmutableHashSet<string>>.Empty;

    /// <summary>Mods marked favourite, from the same metadata store as the tags.</summary>
    public ImmutableHashSet<ModId> FavouriteIds { get; init; } = [];

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Search) && Source is null
        && !WarningsOnly
        && SelectedTagIds.IsEmpty && !UntaggedOnly && !FavouritesOnly;
}

/// <summary>Pure matcher: does a mod pass the current criteria? All active predicates AND together.</summary>
public static class RowFilter
{
    public static bool Matches(Mod mod, FilterCriteria c)
    {
        if (c.Source is { } src && mod.Source != src) return false;
        if (c.WarningsOnly && !c.WarnedIds.Contains(mod.PackageId)) return false;
        if (!MatchesTags(mod, c)) return false;
        if (!MatchesSearch(mod, c)) return false;
        return true;
    }

    private static bool MatchesTags(Mod mod, FilterCriteria c)
    {
        if (c.SelectedTagIds.IsEmpty && !c.UntaggedOnly && !c.FavouritesOnly) return true;

        c.TagsByMod.TryGetValue(mod.PackageId, out var carried);
        var isUntagged = carried is null || carried.Count == 0;
        var isFavourite = c.FavouriteIds.Contains(mod.PackageId);

        // Both pseudo-tags combine under the SAME any/all rule as the real ones, so
        // "Match all + Untagged + a real tag" stays provably empty rather than being
        // quietly redefined into something that looks like it worked.
        if (c.MatchAllTags)
            return (!c.UntaggedOnly || isUntagged)
                && (!c.FavouritesOnly || isFavourite)
                && c.SelectedTagIds.All(id => carried is not null && carried.Contains(id));

        return (c.UntaggedOnly && isUntagged)
            || (c.FavouritesOnly && isFavourite)
            || c.SelectedTagIds.Any(id => carried is not null && carried.Contains(id));
    }

    // IsUnsupported went with the "Unsupported" chip (N2 - UI-7). The CHECK did not go
    // anywhere: ModListValidator.CheckVersions is where it belongs, and it now runs over
    // the inactive pane too. So an unsupported mod carries a warning on its row and
    // appears in the dock's UNSUPPORTED VERSION group, instead of being findable only by
    // remembering to press a chip.

    private static bool MatchesSearch(Mod mod, FilterCriteria c)
    {
        if (string.IsNullOrWhiteSpace(c.Search)) return true;

        var haystacks = new[] { mod.Name, mod.PackageId.Display, string.Join(" ", mod.Authors) };

        if (c.UseRegex)
        {
            Regex regex;
            try
            {
                regex = new Regex(c.Search, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                return true; // an incomplete/invalid pattern shouldn't hide everything mid-typing
            }

            return haystacks.Any(h => h is not null && regex.IsMatch(h));
        }

        return haystacks.Any(h => h is not null && h.Contains(c.Search, StringComparison.OrdinalIgnoreCase));
    }
}
