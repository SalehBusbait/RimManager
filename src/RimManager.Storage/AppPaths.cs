namespace RimManager.Storage;

/// <summary>Well-known locations for RimManager's own data (global, non-portable).</summary>
public static class AppPaths
{
    /// <summary><c>%LocalAppData%/RimManager</c> (or the platform equivalent).</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RimManager");

    /// <summary>Disposable derived caches: <c>&lt;Root&gt;/cache</c>. Safe to delete.</summary>
    public static string CacheDir => Path.Combine(Root, "cache");

    /// <summary>The synced community-rules snapshot: <c>&lt;Root&gt;/cache/communityRules.json</c>.
    /// A re-downloadable cache, not source-of-truth.</summary>
    public static string CommunityRulesCachePath => Path.Combine(CacheDir, "communityRules.json");

    /// <summary>The synced UseThisInstead database (N7), stored decompressed:
    /// <c>&lt;Root&gt;/cache/replacements.json</c>.</summary>
    public static string ReplacementsCachePath => Path.Combine(CacheDir, "replacements.json");

    /// <summary>The synced NoVersionWarning list for one game version (N7):
    /// <c>&lt;Root&gt;/cache/modIdsToFix-1.6.xml</c>. Per version, like the upstream.</summary>
    public static string KnownGoodCachePath(string gameMajorMinor) =>
        Path.Combine(CacheDir, $"modIdsToFix-{gameMajorMinor}.xml");

    /// <summary>RimManager's private SteamCMD instance: <c>&lt;Root&gt;/steamcmd</c>.
    /// Isolated from any system/RimSort install.</summary>
    public static string SteamCmdDir => Path.Combine(Root, "steamcmd");

    /// <summary>Staging dir SteamCMD downloads land in before relocation:
    /// <c>&lt;Root&gt;/cache/steamcmd-downloads</c>.</summary>
    public static string SteamCmdDownloadsDir => Path.Combine(CacheDir, "steamcmd-downloads");

    /// <summary>
    /// Timestamped copies of the previous <c>ModsConfig.xml</c>: <c>&lt;Root&gt;/backups</c> (O5).
    /// <para>
    /// NOT under <c>cache/</c>, deliberately, and this is the one place that distinction
    /// bites: the storage rule says a cache must be deletable without losing anything,
    /// and these are the only copy of a load order the user may want back. They are user
    /// data that happens to be derived, not a cache.
    /// </para>
    /// </summary>
    public static string BackupsDir => Path.Combine(Root, "backups");
}
