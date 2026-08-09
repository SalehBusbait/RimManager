namespace RimManager.Core.Domain;

/// <summary>
/// Which files in RimWorld's <c>Config</c> folder belong to a modlist, and which belong to
/// the game and must never travel with one.
/// <para>
/// A <b>deny</b>-list, not an allow-list, and that is a decision taken from a real install.
/// The obvious rule — capture <c>Mod_*.xml</c> — covers 397 of the 418 files there but
/// silently misses every mod that writes its own filename: <c>CameraPlusColors.txt</c>,
/// <c>CameraPlusDefaultRules.xml</c>, <c>VanillaBackgroundsExpanded_LoadingSettings.txt</c>,
/// <c>ModSettingsFrameworkMod_Settings.xml</c>, <c>TrueTerrainColorsCache.xml</c>. Missing
/// those means a switch silently loses tuning, which is exactly the failure this feature
/// exists to prevent.
/// </para>
/// <para>
/// The denied set is small, well known and load-bearing. <c>Prefs.xml</c> and
/// <c>KeyPrefs.xml</c> sit in this same folder, and restoring them on a modlist switch
/// would change the player's screen resolution, volume and keybindings — a spectacular
/// way to lose someone's trust. <c>ModsConfig.xml</c> is the load order, which the apply
/// pipeline owns; restoring it here would have two writers fighting over one file.
/// </para>
/// </summary>
public static class ModSettingsFiles
{
    /// <summary>
    /// Game-owned, never captured and never restored. Matched case-insensitively because
    /// this runs on three platforms and only one of them cares.
    /// </summary>
    public static readonly IReadOnlySet<string> GameOwned =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ModsConfig.xml",       // the load order — the apply pipeline owns it
            "Prefs.xml",            // resolution, volume, autosave interval
            "KeyPrefs.xml",         // keybindings
            "LastPlayedVersion.txt",
            "Knowledge.xml",        // vanilla learning-helper progress: player state
        };

    /// <summary>Whether this file travels with a modlist.</summary>
    public static bool ShouldCapture(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (GameOwned.Contains(fileName)) return false;

        // Our own timestamped backups of ModsConfig.xml, and anything else's.
        if (fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return false;

        // A backup pattern like ModsConfig.xml.20260725T133751Z.bak is caught above, but
        // guard the stem too so a future backup naming scheme cannot smuggle the load
        // order into a settings snapshot.
        return !GameOwned.Any(g =>
            fileName.StartsWith(g + ".", StringComparison.OrdinalIgnoreCase));
    }
}
