using System.Collections.Generic;
using System.Linq;
using RimManager.Core.Analysis;

namespace RimManager.App.ViewModels;

/// <summary>
/// The conflict vocabulary shared by every surface that still speaks it: the kind
/// labels, the harmless rule, and the one-line summary.
/// <para>
/// N6c slimmed this from the 2c dock tab's presenter — the grouped table, the kind
/// chips and the resolution chain went with the tab, replaced by the ⚡ marks
/// (<see cref="RowConflicts"/>) and the per-mod window
/// (<see cref="ModConflictsPresenter"/>), both of which consume what remains here.
/// </para>
/// </summary>
public static class ConflictsPresenter
{
    /// <summary>Most-impactful first: Harmony code, then Def, XML patch, texture.</summary>
    private static int Priority(ConflictKind kind) => kind switch
    {
        ConflictKind.HarmonyPatch => 0,
        ConflictKind.DefOverride => 1,
        ConflictKind.PatchCollision => 2,
        _ => 3, // TextureCollision
    };

    public static string KindLabel(ConflictKind kind) => kind switch
    {
        ConflictKind.HarmonyPatch => "Harmony",
        ConflictKind.DefOverride => "Def override",
        ConflictKind.PatchCollision => "XML patch",
        _ => "Texture",
    };

    /// <summary>
    /// A conflict where every provider ships identical markup: the overlap is real but
    /// changes nothing, and every conflict surface hides these (214 of 252 on the
    /// design's example install), leaving only the decisions.
    /// <para>
    /// A provider whose XML could not be captured — a texture, or an unreadable file —
    /// makes the conflict <b>not</b> harmless. "We could not tell" must never render as
    /// "nothing to see here".
    /// </para>
    /// </summary>
    public static bool IsHarmless(ModConflict conflict)
    {
        var providers = conflict.ProvidersOrEmpty;
        if (providers.Length < 2) return false;
        if (providers.Any(p => p.Xml is null)) return false;

        var first = Normalize(providers[0].Xml!);
        return providers.All(p => Normalize(p.Xml!) == first);
    }

    private static string Normalize(string xml) =>
        string.Join('\n', xml.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim())).Trim();

    /// <summary>One-line summary by kind, e.g. "2 Harmony · 3 Def override · 1 Texture";
    /// "No conflicts detected." when empty.</summary>
    public static string Summarize(IReadOnlyCollection<ModConflict> conflicts)
    {
        if (conflicts.Count == 0) return "No conflicts detected.";

        var parts = conflicts
            .GroupBy(c => c.Kind)
            .OrderBy(g => Priority(g.Key))
            .Select(g => $"{g.Count()} {KindLabel(g.Key)}");

        return string.Join(" · ", parts);
    }
}
