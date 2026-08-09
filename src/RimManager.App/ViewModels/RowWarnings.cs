using System.Collections.Immutable;

namespace RimManager.App.ViewModels;

/// <summary>
/// What a row's warning glyph says when you hover it.
/// <para>
/// It used to say <c>"Has warnings"</c> — which is exactly what the glyph itself
/// already said, in colour. The dock knew the answer the whole time; the row simply
/// never carried it (N2 · UI-4).
/// </para>
/// <para>
/// Pure and separate because a tooltip is the one piece of UI text nobody screenshots:
/// it is only ever seen by someone who already suspects something, so its wording has
/// to be right the first time rather than reviewed into shape.
/// </para>
/// </summary>
/// <summary>
/// One warning as mod info draws it: the sentence, plus the tone that picks its glyph.
/// <para>
/// The tone is carried per LINE, not per row. Mod info hardcoded the amber ⚠ for every
/// warning, so Achtung!'s row showed the red blocking mark in the list and an amber
/// triangle in the pane for the very same incompatibility — the panel disagreeing with
/// the row about how serious something is.
/// </para>
/// </summary>
public sealed record RowWarning(string Message, WarningTone Tone)
{
    public bool IsBlocking => Tone == WarningTone.Blocking;
    public bool IsWarning => Tone == WarningTone.Warning;
    public bool IsInfo => Tone == WarningTone.Info;
}

public static class RowWarnings
{
    /// <summary>
    /// How many warnings a tooltip lists before it starts counting instead.
    /// <para>
    /// Four, because a tooltip that runs past the window edge is dismissed unread, and
    /// because the fifth line of a list nobody asked for is not what makes someone act.
    /// The remainder is counted rather than dropped: "and 9 more" is a different claim
    /// from silence.
    /// </para>
    /// </summary>
    public const int MaxListed = 4;

    /// <summary>
    /// The tooltip for a row carrying <paramref name="messages"/>.
    /// <para>
    /// One warning is written as itself, with no count and no bullet: a "1 warning:"
    /// header above a single line is a heading for nothing. Two or more get the count
    /// first, because the number is the part that changes what you do next.
    /// </para>
    /// </summary>
    public static string Tip(IReadOnlyList<RowWarning> messages)
    {
        if (messages.Count == 0) return string.Empty;
        if (messages.Count == 1) return messages[0].Message;

        var listed = messages.Take(MaxListed).Select(m => $"• {m.Message}");
        var remainder = messages.Count - MaxListed;

        var lines = remainder > 0
            ? listed.Append($"• and {remainder} more")
            : listed;

        return $"{messages.Count} warnings:{Environment.NewLine}"
               + string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The mod-info section heading. Carries the count for the same reason the tooltip
    /// does, and is singular at one — a section labelled "1 WARNINGS" reads as a bug in
    /// the app, which is a poor thing to be reading about in a panel about problems.
    /// </summary>
    public static string SectionHeading(int count) =>
        count == 1 ? "1 WARNING" : $"{count} WARNINGS";

    /// <summary>
    /// Trims the trailing full stop off a validator sentence so a bulleted list does not
    /// read as a paragraph broken into pieces. Anything not ending in one is untouched.
    /// </summary>
    public static ImmutableArray<RowWarning> ForList(IEnumerable<RowWarning> messages) =>
        [.. messages.Select(m => m with
        {
            Message = m.Message.EndsWith('.') ? m.Message[..^1] : m.Message,
        })];
}
