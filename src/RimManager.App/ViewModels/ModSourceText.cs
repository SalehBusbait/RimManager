using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// How a <see cref="ModSource"/> is written for a human.
/// <para>
/// It exists because <c>ModSource.ToString()</c> was being shown directly, and the
/// enum member is spelled <c>Dlc</c> — so every DLC row's tooltip and every DLC mod
/// info pill read "Dlc", which is not a word. An enum member name is an identifier,
/// never display text.
/// </para>
/// <para>
/// Two forms, because the two places want different lengths: <see cref="Label"/> for
/// the mod-info pill, which sits beside the glyph in a 18px chip, and
/// <see cref="Describe"/> for the row badge's tooltip, which is the ONLY thing naming
/// the source now that the badge is an icon rather than a letter.
/// </para>
/// </summary>
public static class ModSourceText
{
    /// <summary>Short form: one or two words, correctly cased.</summary>
    public static string Label(ModSource source) => source switch
    {
        ModSource.Core => "Core",
        ModSource.Dlc => "DLC",
        ModSource.Workshop => "Workshop",
        ModSource.Local => "Local",
        ModSource.Git => "Git",
        _ => "Unknown",
    };

    /// <summary>
    /// Long form for the badge tooltip. It says where the files came FROM, because
    /// that is the question the column answers — and with the letter gone it is the
    /// only place the answer is written out.
    /// </summary>
    public static string Describe(ModSource source) => source switch
    {
        ModSource.Core => "Core — RimWorld itself",
        ModSource.Dlc => "DLC — an official expansion",
        ModSource.Workshop => "Workshop — subscribed through Steam",
        ModSource.Local => "Local — a folder in the game's Mods directory",
        ModSource.Git => "Git — a clone you track yourself",
        _ => "Unknown source",
    };
}
