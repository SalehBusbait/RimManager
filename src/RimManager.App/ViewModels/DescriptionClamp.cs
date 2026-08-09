namespace RimManager.App.ViewModels;

/// <summary>
/// Whether Mod Info's four-line description clamp is actually cutting anything —
/// which is what decides if "Read more" is offered.
/// </summary>
/// <remarks>
/// An ESTIMATE, and deliberately a generous one. The exact answer needs the rendered
/// text, which lives in the view and is not available when the view model is built;
/// the two ways of being wrong are not equally bad. Offering the button on a
/// description that happened to fit costs a window showing the same words the user
/// already read. Withholding it on one that was cut leaves text unreachable with no
/// sign it exists. So the characters-per-line figure below is pessimistic on purpose.
/// </remarks>
public static class DescriptionClamp
{
    /// <summary>The <c>MaxLines</c> the pane's description TextBlock is given.</summary>
    public const int MaxLines = 4;

    /// <summary>
    /// Conservative characters per rendered line: the info pane's text column is
    /// ~324px at the 10px small style, which fits comfortably more than this. Under-
    /// counting means over-offering, which is the harmless direction.
    /// </summary>
    private const int CharsPerLine = 44;

    public static bool IsClamped(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;

        var lines = 0;
        foreach (var paragraph in description.Split('\n'))
        {
            // An empty paragraph is still a line on screen.
            var length = paragraph.TrimEnd('\r').Length;
            lines += Math.Max(1, (length + CharsPerLine - 1) / CharsPerLine);
            if (lines > MaxLines) return true;
        }

        return false;
    }
}
