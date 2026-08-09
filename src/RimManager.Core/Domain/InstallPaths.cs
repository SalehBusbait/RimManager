using System.Text.Json.Serialization;

namespace RimManager.Core.Domain;

/// <summary>
/// Where the one RimWorld install lives. Replaces <c>InstancePaths</c>: there is a single
/// install, edited in Settings ▸ Paths, and modlists are what the user switches between
/// (the modlist migration).
/// <para>
/// Multi-install was considered and dropped. The case rests on version pinning, and
/// RimWorld tolerates version mismatch in a way Bethesda games do not — <c>loadFolders</c>,
/// XML and texture mods commonly work across versions, and the game warns rather than
/// fails. Adding named path sets later is additive; shipping a concept nobody needs is not.
/// </para>
/// </summary>
public sealed record InstallPaths
{
    public required string GameDir { get; init; }

    /// <summary>
    /// Holds <c>ModsConfig.xml</c> <b>and</b> every <c>Mod_*.xml</c> settings file — which
    /// is why capturing mod settings with a modlist is possible at all, and why an
    /// "instance" pointing somewhere else was never isolation: RimWorld reads a relocated
    /// config folder only when launched with <c>-savedatafolder</c>.
    /// </summary>
    public string? ConfigDir { get; init; }

    public string? WorkshopDir { get; init; }
    public string? SteamCmdDir { get; init; }

    [JsonIgnore] public string LocalModsDir => System.IO.Path.Combine(GameDir, "Mods");
    [JsonIgnore] public string DataDir => System.IO.Path.Combine(GameDir, "Data");

    /// <summary>The game's active-mods file, or null when no config folder is known.</summary>
    [JsonIgnore]
    public string? ModsConfigPath =>
        ConfigDir is null ? null : System.IO.Path.Combine(ConfigDir, "ModsConfig.xml");
}
