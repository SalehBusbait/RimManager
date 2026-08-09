using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// One layout trap, stated exactly.
/// <para>
/// A horizontal <c>StackPanel</c> measures its children with <b>infinite</b> width, so
/// a <c>TextBlock</c> inside one never runs out of room and its <c>TextTrimming</c> can
/// never engage. The text then paints past whatever cell the StackPanel is in. It is
/// invisible on a wide window and unmissable on a narrow one — which is how three dock
/// tables shipped with the sentence in the ISSUE column painted over the MOD column,
/// found only when 2k's 900px breakpoint made the columns tight.
/// </para>
/// <para>
/// R6 wrote a guard for the related <c>TextTrimming</c>-without-a-width-bound case and
/// DELETED it: it flagged 29 correct sites for one wrong one, because whether a bound
/// exists depends on the whole ancestor chain. This rule has no such ambiguity. Inside
/// an infinite-width parent the ancestor chain cannot rescue anything, so there is
/// exactly one escape — a <c>Width</c> or <c>MaxWidth</c> on the TextBlock itself — and
/// the exception is exhaustive rather than a judgement call.
/// </para>
/// </summary>
public sealed class LayoutTrapTests
{
    [Fact]
    public void No_TextTrimming_inside_a_horizontal_StackPanel_where_it_can_never_engage()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (var line in TrimmingInsideAHorizontalStack(File.ReadAllText(file)))
                offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{line}");
        }

        offenders.Should().BeEmpty(
            "a horizontal StackPanel gives its children infinite width, so TextTrimming "
            + "there never engages and the text paints outside its cell. Use a Grid with "
            + "a star column for the part that should ellipsize.");
    }

    /// <summary>
    /// Walks the markup as a tag stream, tracking how deep we are inside horizontal
    /// StackPanels. Comments are stripped first — a design note showing markup is not
    /// markup.
    /// </summary>
    private static IEnumerable<int> TrimmingInsideAHorizontalStack(string markup)
    {
        markup = Regex.Replace(markup, "<!--.*?-->", m => Blank(m.Value), RegexOptions.Singleline);

        var depth = 0;                  // open horizontal StackPanels, in THIS layout root
        var stack = new Stack<char>();  // per open element: 'h' horizontal stack, 'f' flyout, '-' other
        var popupDepths = new Stack<int>();  // outer depth, parked while inside a flyout
        var index = 0;

        while ((index = markup.IndexOf('<', index)) >= 0)
        {
            var end = markup.IndexOf('>', index);
            if (end < 0) yield break;

            var tag = markup[index..(end + 1)];
            index = end + 1;

            if (tag.StartsWith("<!", StringComparison.Ordinal)
                || tag.StartsWith("<?", StringComparison.Ordinal)) continue;

            if (tag.StartsWith("</", StringComparison.Ordinal))
            {
                if (stack.Count == 0) continue;
                switch (stack.Pop())
                {
                    case 'h': depth--; break;
                    case 'f': depth = popupDepths.Count > 0 ? popupDepths.Pop() : 0; break;
                }

                continue;
            }

            var selfClosing = tag.EndsWith("/>", StringComparison.Ordinal);
            var name = Regex.Match(tag, @"^<\s*([\w.:]+)").Groups[1].Value;

            // A property element (<Grid.ColumnDefinitions>) is not a control; its own
            // close tag still has to pop, so it is pushed as "not a stack panel".
            var isHorizontalStack =
                name.EndsWith("StackPanel", StringComparison.Ordinal)
                && tag.Contains("Orientation=\"Horizontal\"", StringComparison.Ordinal);

            // A flyout's content is a POPUP — its own layout root, measured against the
            // popup, not against whatever the opening control happens to sit in. So the
            // enclosing depth does not reach inside it, and counting it would flag the
            // tag flyout purely for hanging off a chip that lives in the toolbar's
            // horizontal run. Tracked as a depth SNAPSHOT rather than a reset, because
            // markup after the flyout closes is back in the outer layout.
            if (name.EndsWith("Flyout", StringComparison.Ordinal) && !selfClosing)
            {
                popupDepths.Push(depth);
                depth = 0;
                stack.Push('f');
                continue;
            }

            // The ONE thing that rescues it: a Width or MaxWidth on the TextBlock
            // itself. Inside an infinite-width parent nothing else can bound the
            // measure, so this exception is exhaustive rather than a judgement call.
            var boundedItself =
                tag.Contains("MaxWidth=", StringComparison.Ordinal)
                || Regex.IsMatch(tag, @"\sWidth=""[\d.]");

            if (depth > 0
                && name.EndsWith("TextBlock", StringComparison.Ordinal)
                && tag.Contains("TextTrimming=", StringComparison.Ordinal)
                && !boundedItself)
            {
                yield return LineOf(markup, index);
            }

            if (selfClosing) continue;

            stack.Push(isHorizontalStack ? 'h' : '-');
            if (isHorizontalStack) depth++;
        }
    }

    /// <summary>
    /// A second trap, stated just as exactly: <b><c>Padding</c> on a <c>ScrollViewer</c> is
    /// subtracted from the scrollable extent instead of added to it.</b>
    /// <para>
    /// Measured on Settings ▸ Advanced against the real install: the page's
    /// <c>DesiredSize.Height</c> was 667, the ScrollViewer's extent came back <b>635</b> —
    /// short by exactly the 32px of vertical padding — and the content was arranged at 635
    /// with the remainder clipped. Worse than a clip: 635 is below the 640 viewport, so
    /// Avalonia concluded there was nothing to scroll and <b>never showed a scrollbar</b>.
    /// The danger zone's bottom edge was unreachable by any means. Integrations had the
    /// same defect at 637 vs 635 and nobody had noticed, because two pixels look like a
    /// margin.
    /// </para>
    /// <para>
    /// No judgement call and no ancestor chain to consider: the gutter belongs on the
    /// content as a <c>Margin</c>, where it is measured as part of the content and lands in
    /// the extent. Every page under a <c>MinHeight</c> smaller than its content is exposed,
    /// which here is all seven.
    /// </para>
    /// </summary>
    [Fact]
    public void No_ScrollViewer_sets_Padding_because_it_never_reaches_the_extent()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var markup = Regex.Replace(
                File.ReadAllText(file), "<!--.*?-->", m => Blank(m.Value), RegexOptions.Singleline);

            foreach (var (tag, index, _) in Tags(markup))
            {
                if (!Regex.IsMatch(tag, @"^<\s*ScrollViewer[\s/>]")) continue;
                if (!tag.Contains("Padding=", StringComparison.Ordinal)) continue;

                offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{LineOf(markup, index)}");
            }
        }

        offenders.Should().BeEmpty(
            "ScrollViewer.Padding is subtracted from the scrollable extent rather than "
            + "added to it, so the content's last N pixels can never be scrolled to — and "
            + "when that pushes the extent below the viewport, the scrollbar disappears "
            + "too. Put the gutter on the content as a Margin.");
    }

    /// <summary>
    /// "Icon-only controls have automation names" — the handoff's definition of done
    /// for every screen.
    /// <para>
    /// A screen reader announces a Button by its content. An icon-only one has no
    /// content to announce, so it reads as "button" and nothing else; a TextBox, a
    /// ComboBox or a ListBox with its label in a neighbouring grid cell reads as
    /// "edit"/"combo box"/"list" with no name at all, because Avalonia does not infer
    /// one from an adjacent TextBlock.
    /// </para>
    /// <para>
    /// Controls whose Content is text — including a binding, which resolves to text at
    /// runtime — are named by that content and are not listed here.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_unlabelled_control_carries_an_automation_name()
    {
        string[] inputs = ["TextBox", "ComboBox", "ListBox", "AutoCompleteBox"];
        string[] buttons = ["Button", "ToggleButton", "RepeatButton", "SplitButton"];

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var markup = Regex.Replace(
                File.ReadAllText(file), "<!--.*?-->", m => Blank(m.Value), RegexOptions.Singleline);

            foreach (var (tag, index, bodyEnd) in Tags(markup))
            {
                var name = Regex.Match(tag, @"^<\s*([\w.:]+)").Groups[1].Value;
                if (tag.Contains("AutomationProperties.Name", StringComparison.Ordinal)) continue;

                var named = Regex.IsMatch(tag, @"\bContent=""[^""]");
                if (inputs.Contains(name) && !named)
                {
                    offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{LineOf(markup, index)} {name}");
                    continue;
                }

                if (!buttons.Contains(name) || named) continue;

                // Icon-only: an icon inside, and no text of its own to be named by.
                var close = markup.IndexOf($"</{name}>", bodyEnd, StringComparison.Ordinal);
                var body = close > 0 ? markup[bodyEnd..close] : string.Empty;
                var iconOnly =
                    (body.Contains("PathIcon", StringComparison.Ordinal)
                     || body.Contains("<Image", StringComparison.Ordinal))
                    && !Regex.IsMatch(body, @"<TextBlock[^>]*Text=""[^""]");

                if (iconOnly)
                    offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{LineOf(markup, index)} {name}");
            }
        }

        offenders.Should().BeEmpty(
            "a control with no text of its own is announced as its type and nothing else. "
            + "Give it AutomationProperties.Name.");
    }

    private static IEnumerable<(string Tag, int Start, int End)> Tags(string markup)
    {
        var index = 0;
        while ((index = markup.IndexOf('<', index)) >= 0)
        {
            var end = markup.IndexOf('>', index);
            if (end < 0) yield break;

            yield return (markup[index..(end + 1)], index, end + 1);
            index = end + 1;
        }
    }

    private static string Blank(string comment) =>
        new(comment.Select(c => c == '\n' ? '\n' : ' ').ToArray());

    private static int LineOf(string text, int index) =>
        text[..index].Count(c => c == '\n') + 1;
}
