using RimManager.Core.Domain;

namespace RimManager.Core.Sharing;

/// <summary>
/// Turns a Workshop-item-borne <c>.rwlist</c> into a <b>new modlist's</b> arrangement
/// (NF-10, T7 decision 3: the import never touches the current list). The entries
/// array is reproduced verbatim — order, separators, palette indexes, enabled states —
/// and each mod entry keeps the identity the list carries (source, Workshop id, git
/// origin, version), which is what lets the new list name mods the recipient has not
/// installed yet.
/// </summary>
public static class RwListWorkshopImport
{
    public static ModlistState ToState(RwList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var entries = new List<ModlistEntry>(list.Entries.Length);
        var separator = 0;
        foreach (var entry in list.Entries)
        {
            if (entry.Type == RwEntryKind.Separator)
            {
                separator++;
                // PaletteIndex is authoritative; a list written by another tool may
                // carry only the advisory hex, which maps to the nearest hue.
                entries.Add(ModlistEntry.Separator(
                    entry.Id ?? $"sep-{separator}",
                    entry.Name ?? "Separator",
                    entry.PaletteIndex ?? Palette.NearestTo(entry.Color),
                    entry.Collapsed));
                continue;
            }

            // A mod entry without a packageId cannot be addressed by anything
            // downstream (drift, apply, reconcile) — dropped rather than guessed at.
            if (string.IsNullOrWhiteSpace(entry.PackageId)) continue;

            var id = ModId.From(entry.PackageId);
            entries.Add(new ModlistEntry(
                ModlistEntryKind.Mod,
                id.Value,
                string.IsNullOrWhiteSpace(entry.DisplayName) ? id.Display : entry.DisplayName,
                Enabled: true,
                Source: Map(entry.Source),
                PublishedFileId: entry.PublishedFileId,
                GitUrl: entry.GitUrl,
                GitRef: entry.GitRef,
                ModVersion: entry.ModVersion));
        }

        return ModlistState.Empty.WithEntries(entries);
    }

    /// <summary>
    /// The new modlist's name: the list's own, else the file's, made unique the way
    /// every other generated name is — a suffix, never a silent overwrite.
    /// </summary>
    public static string UniqueName(string? listName, string fileName, IEnumerable<string> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var basis = !string.IsNullOrWhiteSpace(listName)
            ? listName.Trim()
            : Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(basis)) basis = "Imported list";

        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(basis)) return basis;

        for (var n = 2; ; n++)
        {
            var candidate = $"{basis} ({n})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static ModSource Map(RwSource source) => source switch
    {
        RwSource.Workshop => ModSource.Workshop,
        RwSource.Local => ModSource.Local,
        RwSource.Git => ModSource.Git,
        RwSource.Dlc => ModSource.Dlc,
        // Still accepted, deliberately: the vault is gone (O13) but lists exported
        // before it went — and lists from other tools — carry the value. A pinned copy
        // was a full folder under our own root, so Local is what it becomes.
        RwSource.Pinned => ModSource.Local,
        _ => ModSource.Unknown,
    };
}
