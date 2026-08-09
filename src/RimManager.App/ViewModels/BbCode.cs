using System.Text;
using System.Text.RegularExpressions;

namespace RimManager.App.ViewModels;

/// <summary>
/// Strips Steam Workshop BBCode out of a mod description.
/// <para>
/// About.xml descriptions are pasted straight from Workshop pages, so a great many
/// carry <c>[b]</c>, <c>[url=…]</c>, <c>[list]</c> and friends. Rendering them raw
/// fills the info pane with markup instead of prose — the pane's job is a clamped
/// four-line summary (1a §8), and markup makes those four lines worthless.
/// </para>
/// <para>
/// Deliberately a stripper, not a renderer: RimManager does not need rich text here,
/// and a half-working renderer is worse than clean plain text.
/// </para>
/// </summary>
public static partial class BbCode
{
    /// <summary>
    /// Matches only KNOWN BBCode tags, opening or closing, with or without an
    /// attribute — <c>[b]</c>, <c>[/url]</c>, <c>[url=https://…]</c>.
    /// <para>
    /// An allowlist rather than "anything in square brackets" because mod names
    /// genuinely use brackets: <c>[sbz] Neat Storage</c>, <c>[1.5] Some Mod</c>.
    /// A greedy pattern eats those and mangles real text.
    /// </para>
    /// </summary>
    [GeneratedRegex(
        @"\[/?(?:b|i|u|s|strike|url|img|list|olist|quote|code|noparse|spoiler|h1|h2|h3|table|tr|td|previewyoutube)(?:=[^\]]*)?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TagPattern();

    /// <summary>Three or more consecutive newlines collapse to a paragraph break.</summary>
    [GeneratedRegex(@"(\r?\n){3,}", RegexOptions.Compiled)]
    private static partial Regex ExcessBreaks();

    public static string? Strip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // A list item loses its marker along with its tag, so put one back — the
        // bullets are usually the only structure worth keeping.
        var withBullets = text.Replace("[*]", "· ", StringComparison.OrdinalIgnoreCase);

        var stripped = TagPattern().Replace(withBullets, string.Empty);
        var tidied = ExcessBreaks().Replace(stripped, "\n\n").Trim();

        return tidied.Length == 0 ? null : Collapse(tidied);
    }

    /// <summary>Collapses runs of spaces and tabs left behind by removed tags.</summary>
    private static string Collapse(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text)
        {
            var isSpace = c is ' ' or '\t';
            if (isSpace && lastWasSpace) continue;
            sb.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }

        return sb.ToString();
    }
}
