namespace RimManager.App.ViewModels;

/// <summary>
/// A mod's whole description, for when Mod Info's four-line clamp has cut it (O3).
/// <para>
/// The same text the pane shows, not a second reading of the file: BBCode is stripped
/// once, upstream in <see cref="ModDetailViewModel"/>, so the window and the pane
/// cannot render the mod differently. A reference window like the image viewer —
/// non-modal, read-only, Escape closes.
/// </para>
/// </summary>
public sealed class DescriptionViewerViewModel
{
    public DescriptionViewerViewModel(string modName, string description)
    {
        Title = modName;
        Description = description;
        FooterText = Footer(description);
    }

    /// <summary>The window title — the mod's name, so the taskbar names the mod.</summary>
    public string Title { get; }

    public string Description { get; }

    /// <summary>"1,240 words · 8,132 characters".</summary>
    public string FooterText { get; }

    /// <summary>
    /// Static and pure so the wording is testable without a window. Words are
    /// whitespace-separated runs — the ordinary meaning, and the only one that does
    /// not need a language model to be right.
    /// </summary>
    public static string Footer(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "empty";

        var words = description.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        return $"{words:N0} {(words == 1 ? "word" : "words")} · {description.Length:N0} characters";
    }
}
