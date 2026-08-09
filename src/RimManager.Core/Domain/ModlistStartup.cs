using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>
/// One row of the active pane: the list's entry, and the installed mod it resolved to.
/// <see cref="Mod"/> is null when the list names something that is not installed — which
/// is a row the user must still see, not one to drop silently.
/// </summary>
public sealed record StartupRow(ModlistEntry Entry, Mod? Mod)
{
    public bool IsMissing => Entry.Kind == ModlistEntryKind.Mod && Mod is null;
}

/// <summary>What to put on screen when a modlist is opened.</summary>
public sealed record StartupPlan
{
    public ImmutableArray<StartupRow> Active { get; init; } = [];
    public ImmutableArray<Mod> Inactive { get; init; } = [];
    public ImmutableArray<ModlistEntry> Missing { get; init; } = [];

    /// <summary>Mods installed but not named by the list, plus the list's own missing mods.</summary>
    public bool HasMissing => !Missing.IsEmpty;
}

/// <summary>
/// Turns a modlist plus a scan into the two panes.
/// <para>
/// This is the change that makes separators survive a restart. Startup used to rebuild the
/// active pane from <c>ModsConfig.xml</c> — a flat list of packageIds that cannot express a
/// separator, a group, or a collapsed section — so every piece of organisation the user did
/// was discarded on every launch. The modlist is the source of truth now, and the game's
/// file is an output.
/// </para>
/// <para>
/// Pure, per the project's testable-UI convention: the interesting decisions (ordering,
/// what happens to a mod the list names but the disk does not have, what counts as
/// inactive) are decided here and unit-tested, not inside a view model.
/// </para>
/// </summary>
public static class ModlistStartup
{
    public static StartupPlan Resolve(
        Modlist modlist,
        IReadOnlyDictionary<ModId, Mod> installed,
        IEnumerable<Mod> allInstalled)
    {
        var active = ImmutableArray.CreateBuilder<StartupRow>();
        var missing = ImmutableArray.CreateBuilder<ModlistEntry>();
        var named = new HashSet<ModId>();

        foreach (var entry in modlist.State.Entries)
        {
            if (entry.Kind == ModlistEntryKind.Separator)
            {
                active.Add(new StartupRow(entry, null));
                continue;
            }

            var id = ModId.From(entry.Id);
            named.Add(id);

            if (installed.TryGetValue(id, out var mod))
            {
                active.Add(new StartupRow(entry, mod));
            }
            else
            {
                // Kept, not dropped. The entry carries its own identity — source, Workshop
                // id — so the row can say WHICH mod is missing and offer to fetch it. A
                // silently shortened load order is how someone discovers at 2am that the
                // list they shared does not work.
                active.Add(new StartupRow(entry, null));
                missing.Add(entry);
            }
        }

        // Everything installed that the list does not name. Sorted by name because the
        // inactive pane is a library to search, not an order to respect (#3).
        var inactive = allInstalled
            .Where(m => !named.Contains(m.PackageId))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        return new StartupPlan
        {
            Active = active.ToImmutable(),
            Inactive = inactive,
            Missing = missing.ToImmutable(),
        };
    }

    /// <summary>
    /// The arrangement to seed a brand-new modlist with, taken from the game's current
    /// active order. Used when adopting <c>ModsConfig.xml</c> — the one place the flat
    /// file still feeds the app rather than the other way round.
    /// </summary>
    public static ModlistState FromGame(
        IEnumerable<ModId> activeOrder, IReadOnlyDictionary<ModId, Mod> installed) =>
        ModlistState.Empty.WithEntries(activeOrder.Select(id =>
            installed.TryGetValue(id, out var mod)
                ? ModlistEntry.Mod(mod)
                : ModlistEntry.Mod(id)));
}
