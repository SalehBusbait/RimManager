using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>
/// Parsed <c>ModsConfig.xml</c>: the one file RimWorld actually reads. An ordered
/// list of active <c>packageId</c>s plus the game version string and the set of
/// expansions the game knows about.
/// </summary>
/// <param name="Version">Raw version string, e.g. <c>1.6.4871 rev590</c>.</param>
/// <param name="ActiveMods">Active mods, in load order (order is significant).</param>
/// <param name="KnownExpansions">DLCs the game has registered.</param>
public sealed record ModsConfig(
    string Version,
    ImmutableArray<ModId> ActiveMods,
    ImmutableArray<ModId> KnownExpansions)
{
    /// <summary>The <c>major.minor</c> of <see cref="Version"/> (e.g. <c>1.6</c>), or null if unparseable.</summary>
    public string? MajorMinor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Version)) return null;
            var firstToken = Version.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            var parts = firstToken.Split('.');
            return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : null;
        }
    }
}
