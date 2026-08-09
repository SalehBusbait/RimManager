using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// What the row context menu (<c>2i</c>-8) can offer for the current selection. Pure, so
/// the enabling rules are testable — a menu that offers "Open on Workshop" for a local mod
/// is a dead item, and this menu has nine of them to get right.
/// </summary>
/// <param name="Count">How many rows are selected.</param>
/// <param name="Header">"3 mods selected", or the single mod's name.</param>
/// <param name="CanImportRwList">
/// The single selected row is a recognized mod-list item (NF-10) — the menu is the
/// standing re-offer after the once-per-item strip. Hidden on everything else: a
/// "list" action on an ordinary mod would be a dead item, and any source qualifies
/// here (the Workshop-only rule governs the automatic strip, not a user's own click).
/// </param>
public sealed record RowContextState(
    int Count,
    string Header,
    bool CanDeactivate,
    bool CanActivate,
    bool CanOpenFolder,
    bool CanOpenWorkshop,
    bool CanDeleteFromDisk,
    string? WorkshopId,
    ImmutableArray<ModId> PackageIds,
    bool CanImportRwList = false)
{
    public bool IsEmpty => Count == 0;

    /// <summary>Single-selection actions read badly on a multi-selection and are hidden.</summary>
    public bool IsSingle => Count == 1;
}

/// <summary>Builds <see cref="RowContextState"/> from a selection.</summary>
public static class RowContextMenu
{
    public static RowContextState For(IReadOnlyList<ModRowViewModel> selected, bool fromActivePane)
    {
        if (selected.Count == 0)
        {
            return new RowContextState(0, string.Empty, false, false, false, false, false, null, []);
        }

        var single = selected.Count == 1 ? selected[0] : null;

        // Core and the DLC are the game's own files. Offering to delete them from disk is
        // offering to break the install, so the item is not there at all.
        var deletable = selected.All(r => r.Mod.Source is ModSource.Workshop or ModSource.Local);

        return new RowContextState(
            Count: selected.Count,
            Header: single is not null ? single.Name : $"{selected.Count} mods selected",
            CanDeactivate: fromActivePane,
            CanActivate: !fromActivePane,

            // Opening a folder reveals ONE folder; on a multi-selection there is no
            // sensible answer to which.
            CanOpenFolder: single is not null,
            CanOpenWorkshop: single?.Mod.PublishedFileId is not null,
            CanDeleteFromDisk: deletable,
            WorkshopId: single?.Mod.PublishedFileId,
            PackageIds: [.. selected.Select(r => r.PackageId)],
            CanImportRwList: single?.Mod.IsRwListItem == true);
    }

    /// <summary>
    /// What deleting this selection from disk costs, for the confirmation (<c>2i</c>-6).
    /// Names the count and says the one thing that matters: this is the real folder, and
    /// unlike everything else in the danger zone it is <b>not</b> RimManager's own data.
    /// </summary>
    public static string DeleteConsequence(IReadOnlyList<ModRowViewModel> selected)
    {
        var what = selected.Count == 1
            ? $"“{selected[0].Name}”"
            : $"{selected.Count} mods";

        return $"Permanently deletes {what} from disk. This is the mod's own folder, not "
            + "RimManager's record of it — it cannot be undone, and a subscribed Workshop mod "
            + "will come back the next time Steam syncs unless you unsubscribe too.";
    }
}
