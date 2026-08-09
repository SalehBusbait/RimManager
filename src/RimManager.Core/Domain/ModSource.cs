namespace RimManager.Core.Domain;

/// <summary>Where a mod's files come from. Also drives dedupe precedence when the
/// same <c>packageId</c> exists in more than one place (domain primer §3).</summary>
public enum ModSource
{
    Unknown = 0,

    /// <summary>RimWorld itself: <c>ludeon.rimworld</c> under <c>&lt;game&gt;/Data/Core</c>.</summary>
    Core,

    /// <summary>An official expansion under <c>&lt;game&gt;/Data</c> (Royalty, Ideology, ...).</summary>
    Dlc,

    /// <summary>Steam Workshop item under <c>workshop/content/294100/&lt;id&gt;</c>.</summary>
    Workshop,

    /// <summary>A mod under <c>&lt;game&gt;/Mods</c>.</summary>
    Local,

    /// <summary>A git-tracked mod (distributed outside the Workshop).</summary>
    Git,

    // Pinned is GONE with the version-pinning vault (O13, owner's call). It could only
    // ever be produced by the CLI's `vault pin`, which was the feature's ONLY entry
    // point — so in the GUI the badge was unreachable by construction.
}
