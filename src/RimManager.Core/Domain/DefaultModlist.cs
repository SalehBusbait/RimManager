namespace RimManager.Core.Domain;

/// <summary>What <see cref="DefaultModlist.Reconcile"/> decided, so the caller can persist it.</summary>
/// <param name="Lists">The reconciled set, in the order given.</param>
/// <param name="Changed">
/// The lists whose <see cref="Modlist.IsDefault"/> was altered, and which therefore need
/// writing back. Empty when the invariant already held — the overwhelmingly common case,
/// and the one that must not cost a disk write on every load.
/// </param>
/// <param name="NeedsSeeding">
/// True when there were no lists at all. Core cannot fix this: seeding the default means
/// reading the game's live <c>ModsConfig.xml</c>, which is I/O. The caller does it.
/// </param>
public readonly record struct DefaultReconciliation(
    IReadOnlyList<Modlist> Lists,
    IReadOnlyList<Modlist> Changed,
    bool NeedsSeeding);

/// <summary>
/// Keeps the "there is always exactly one undeletable default modlist" invariant true.
/// <para>
/// Enforced on <b>every load</b>, not only at first-run setup. A rule checked once at
/// creation is a rule that holds until the first time someone edits the folder by hand,
/// restores a backup, or has a write fail half way — and then it is silently false, with
/// the UI offering a Delete button on the last list standing.
/// </para>
/// <para>
/// Pure, so the invariant is decided by a test rather than by whatever the storage layer
/// happened to do. The two I/O-shaped outcomes — seed a default, write back the ones that
/// changed — are returned as data for the caller to carry out.
/// </para>
/// </summary>
public static class DefaultModlist
{
    /// <summary>The name a freshly seeded default gets.</summary>
    public const string SeedName = "Default";

    /// <summary>
    /// Returns the set with exactly one default, and says which entries changed.
    /// <list type="bullet">
    /// <item>No lists at all → <see cref="DefaultReconciliation.NeedsSeeding"/>.</item>
    /// <item>No default → promote one, preferring a list actually named "Default",
    /// otherwise the oldest. Oldest rather than first-by-name so the choice does not
    /// move when an unrelated list is renamed.</item>
    /// <item>More than one default → keep the oldest and demote the rest, for the same
    /// reason: it is the one answer that does not depend on enumeration order.</item>
    /// </list>
    /// </summary>
    public static DefaultReconciliation Reconcile(IReadOnlyList<Modlist> lists)
    {
        if (lists.Count == 0) return new([], [], NeedsSeeding: true);

        var defaults = lists.Where(l => l.IsDefault).ToList();

        // The common path: already exactly one. No writes, no allocation of a new set.
        if (defaults.Count == 1) return new(lists, [], NeedsSeeding: false);

        var keep = defaults.Count == 0 ? Elect(lists) : Oldest(defaults);

        var changed = new List<Modlist>();
        var reconciled = new List<Modlist>(lists.Count);

        foreach (var list in lists)
        {
            var shouldBeDefault = ReferenceEquals(list, keep) || list.Id == keep.Id;
            if (list.IsDefault == shouldBeDefault)
            {
                reconciled.Add(list);
                continue;
            }

            var fixedUp = list with { IsDefault = shouldBeDefault };
            reconciled.Add(fixedUp);
            changed.Add(fixedUp);
        }

        return new(reconciled, changed, NeedsSeeding: false);
    }

    /// <summary>
    /// Which list to promote when none is marked. A list the user already called
    /// "Default" is the one they will expect, whatever its age.
    /// </summary>
    private static Modlist Elect(IReadOnlyList<Modlist> lists) =>
        lists.FirstOrDefault(l => string.Equals(l.Name, SeedName, StringComparison.OrdinalIgnoreCase))
        ?? Oldest(lists);

    /// <summary>
    /// Oldest by creation, with the id as a tiebreak so two lists created in the same
    /// tick — which migration does, in a loop — still resolve to one stable answer
    /// rather than to whichever the filesystem happened to enumerate first.
    /// </summary>
    private static Modlist Oldest(IReadOnlyList<Modlist> lists) =>
        lists.OrderBy(l => l.CreatedUtc)
             .ThenBy(l => l.Id, StringComparer.Ordinal)
             .First();

    /// <summary>
    /// Whether this list may be deleted. The default never may — that is the whole
    /// invariant — and neither may the last one standing, since deleting it would leave
    /// the app with nothing to load and force a seed on the next launch anyway.
    /// </summary>
    public static bool CanDelete(Modlist list, int totalLists) => !list.IsDefault && totalLists > 1;
}
