using System.Collections.Generic;
using System.Threading.Tasks;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// What Settings ▸ Modlists needs from the rest of the app. Replaces
/// <c>IInstanceStore</c>: instances are gone, and after the migration its operations
/// created data nothing reads — a create/duplicate/delete that quietly does nothing is
/// worse than one that is absent.
/// </summary>
public interface IModlistStore
{
    IReadOnlyList<Modlist> All { get; }

    /// <summary>The one the window has open, so its row can say so.</summary>
    string? CurrentId { get; }

    int SnapshotCount(Modlist modlist);

    /// <summary>How many mod-settings files are captured for it; 0 when it captures none.</summary>
    int SettingsFileCount(Modlist modlist);

    Task RenameAsync(Modlist modlist, string name);

    /// <summary>Sets the list's colour dot by palette INDEX (#6).</summary>
    Task RecolourAsync(Modlist modlist, int paletteIndex);

    /// <summary>Copies the arrangement under a new name; the copy shares nothing with it.</summary>
    Task<Modlist> DuplicateAsync(Modlist modlist, string name);

    /// <summary>
    /// Moves the default flag. The only way to change it — reconciliation repairs a broken
    /// set but never reassigns a healthy one — and therefore the only way to make the
    /// current default deletable.
    /// </summary>
    Task SetDefaultAsync(Modlist modlist);

    /// <summary>
    /// Turns capture of in-game mod settings on or off for this list. Turning it OFF
    /// discards what was captured, because keeping a snapshot that nothing will ever
    /// restore is disk pretending to be a feature.
    /// </summary>
    Task SetCapturesModSettingsAsync(Modlist modlist, bool captures);

    /// <summary>Creates an empty list, for "start from nothing".</summary>
    Task<Modlist> CreateAsync(string name);

    /// <summary>
    /// Deletes the list, its snapshots and its captured settings. Never a mod, never a
    /// save, never the game folder — which is what the confirmation promises.
    /// </summary>
    Task DeleteAsync(Modlist modlist);
}
