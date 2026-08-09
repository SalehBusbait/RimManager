using System.Collections.Generic;
using System.Linq;

namespace RimManager.App.ViewModels;

/// <summary>
/// The wording of Settings ▸ Modlists. Pure, so what a destructive confirmation promises
/// is pinned by a test rather than by memory.
/// </summary>
public static class ModlistsPresenter
{
    /// <summary>
    /// What a modlist IS, in one sentence. The page opens with it, because someone who
    /// has only ever had one does not know the word means anything.
    /// </summary>
    public const string WhatIsAModlist =
        "A modlist is a saved load order — which mods are active, in what order, with your "
        + "separators and groups. Switching lists changes what RimWorld loads; it never "
        + "moves, downloads or deletes a mod.";

    /// <summary>
    /// The sentence a delete has to say before it happens. It names what goes AND what
    /// does not: the fear with a mod manager is that it will delete your mods, and this
    /// never touches them.
    /// </summary>
    public static string DeleteConsequence(string name, int snapshots, int settingsFiles)
    {
        var also = (snapshots, settingsFiles) switch
        {
            (0, 0) => string.Empty,
            (_, 0) => $" and its {Plural(snapshots, "snapshot")}",
            (0, _) => $" and its {Plural(settingsFiles, "saved mod-settings file")}",
            _ => $", its {Plural(snapshots, "snapshot")} and its "
                 + $"{Plural(settingsFiles, "saved mod-settings file")}",
        };

        // D5 · curly quotes. The dialog's own TITLE two lines above uses them, so the
        // reader met “My List” and 'My List' in one dialog.
        return $"Deletes the “{name}” list{also}. Your mods, your saves and the game folder "
             + "are untouched.";
    }

    /// <summary>
    /// Why the button is disabled, said out loud. A greyed control with no reason is a
    /// control the user assumes is broken.
    /// </summary>
    public static string? WhyDeleteIsRefused(bool isDefault, int totalLists) => (isDefault, totalLists) switch
    {
        (true, _) => "The default list cannot be deleted. Make another list the default first.",
        (_, <= 1) => "This is your only modlist, and RimManager needs one to load.",
        _ => null,
    };

    /// <summary>What duplicating gives you, so it is not confused with switching.</summary>
    public static string DuplicateConsequence(string name) =>
        $"Creates a copy of “{name}” — same mods, same order, same separators — that you can "
        + "change without affecting the original.";

    /// <summary>
    /// The mod-settings card. Off is the honest default, and the sentence has to say what
    /// turning it on will DO, because it starts writing files in the game's config folder.
    /// </summary>
    public static string ModSettingsSummary(bool captures, int files)
    {
        if (!captures)
        {
            return "Off — this list shares whatever mod settings the game currently has. "
                 + "Turn on to give it its own copy, taken when you switch away from it.";
        }

        return files == 0
            ? "On — settings will be captured the first time you switch away from this list."
            : $"On — {Plural(files, "settings file")} saved with this list.";
    }

    /// <summary>"3 days ago", or "never" for a list that has not been opened.</summary>
    public static string LastUsed(DateTimeOffset? lastUsed, DateTimeOffset now)
    {
        if (lastUsed is not { } when) return "never";

        var age = now - when;
        return age switch
        {
            { TotalMinutes: < 2 } => "just now",
            { TotalHours: < 1 } t => $"{(int)t.TotalMinutes} minutes ago",
            { TotalDays: < 1 } t => $"{Plural((int)t.TotalHours, "hour")} ago",
            { TotalDays: < 30 } t => $"{Plural((int)t.TotalDays, "day")} ago",
            var t => $"{Plural((int)(t.TotalDays / 30), "month")} ago",
        };
    }

    /// <summary>
    /// "Main copy", then "Main copy 2". Distinct by construction rather than by asking:
    /// duplicating is a cheap, exploratory act, and a modal demanding a name before it
    /// will copy anything turns it into a decision.
    /// </summary>
    public static string CopyName(IEnumerable<string> existing, string source)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var basis = $"{source} copy";
        if (!taken.Contains(basis)) return basis;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{source} copy {n}";
            if (!taken.Contains(candidate)) return candidate;
        }

        return basis;
    }

    // D3 · promoted to ViewModels.Plural so the rest of the app can use the house style
    // rather than reinvent "(s)". Kept as a local alias so this file reads unchanged.
    private static string Plural(int n, string noun) => ViewModels.Plural.Of(n, noun);
}
