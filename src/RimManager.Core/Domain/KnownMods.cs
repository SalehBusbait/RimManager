namespace RimManager.Core.Domain;

/// <summary>Well-known packageIds the engine treats specially.</summary>
public static class KnownMods
{
    public static readonly ModId Harmony = ModId.From("brrainz.harmony");
    public static readonly ModId Core = ModId.From("ludeon.rimworld");

    private const string DlcPrefix = "ludeon.rimworld.";

    /// <summary>True for official expansions (<c>ludeon.rimworld.royalty</c>, etc.), but not Core itself.</summary>
    public static bool IsOfficialDlc(ModId id) =>
        id.Value.StartsWith(DlcPrefix, StringComparison.Ordinal) && id != Core;

    /// <summary>
    /// The name to show for the base game and the official expansions, which the
    /// scanner cannot read from <c>About.xml</c>.
    /// <para>
    /// Measured against the real install: <b>every one of Ludeon's own
    /// <c>About.xml</c> files omits <c>&lt;name&gt;</c> entirely</b> — Core, Royalty,
    /// Ideology, Biotech, Anomaly and Odyssey all carry only a packageId, an author
    /// and a version list. The scanner's fallback is the packageId, so without this
    /// the six rows that anchor every load order read
    /// <c>Ludeon.RimWorld.Royalty</c> instead of <c>Royalty</c>.
    /// </para>
    /// <para>
    /// Only Core is mapped. An expansion's name is the last segment of its packageId
    /// in the casing Ludeon authored — so a DLC shipped after this was written is
    /// named correctly with no change here, which a hard-coded table would not be.
    /// </para>
    /// </summary>
    /// <returns>The display name, or <c>null</c> for anything that is not Ludeon's.</returns>
    public static string? DisplayName(ModId id)
    {
        if (id == Core) return "RimWorld";
        if (!IsOfficialDlc(id)) return null;

        var lastDot = id.Display.LastIndexOf('.');
        var segment = lastDot >= 0 ? id.Display[(lastDot + 1)..] : id.Display;
        return segment.Length == 0 ? null : segment;
    }
}
