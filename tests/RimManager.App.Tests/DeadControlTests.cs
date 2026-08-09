using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// A control that renders, takes a click and does nothing is this project's most
/// expensive defect: it builds, it tests green, it launches clean, and it survives
/// phases. <c>File ▸ New Instance</c>, <c>Settings</c> and <c>Quit</c> were dead for
/// four of them; all six tag swatches were dead because <c>CommandParameter="0"</c> is a
/// string; every "open folder" action was dead because the allowlist refused a bare path.
/// <para>
/// N4's audit walked all 287 interactive controls and found four more — mod info's
/// favourite star, Sort ▾'s "Sort selection only", and the primary halves of two
/// SplitButtons, where clicking the label did nothing and only the chevron worked.
/// These two rules are what that walk checked, kept so it need not be walked again.
/// </para>
/// </summary>
public sealed class DeadControlTests
{
    private static readonly string[] Interactive =
        ["Button", "ToggleButton", "MenuItem", "RadioButton", "CheckBox", "SplitButton"];

    /// <param name="Opens">
    /// Whether the element hosts a flyout or context menu as a CHILD element —
    /// <c>&lt;Button.Flyout&gt;</c> rather than an attribute. A control whose whole job
    /// is to open its own menu is wired; reading only the opening tag would call the
    /// separator's ⋮ and mod info's "+ Tag" dead, which they are not.
    /// </param>
    /// <param name="SelfClosing">
    /// Whether the element closed itself (<c>&lt;Button … /&gt;</c>). A self-closing
    /// interactive control has no child content, so whatever it shows must come from an
    /// attribute — which is what makes the legibility check below decidable.
    /// </param>
    private sealed record Control(
        string File, int Line, string Tag, string Attrs, bool Opens, bool SelfClosing);

    private static IEnumerable<Control> Controls()
    {
        foreach (var path in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var text = File.ReadAllText(path);
            // Lookahead for whitespace/slash/close rather than \b: property-element
            // syntax such as <MenuItem.Header> starts with the same word, and \b
            // happily matches before the dot - which reported the Header of a
            // perfectly wired item as a dead control of its own.
            var pattern = "<(" + string.Join('|', Interactive) + @")(?=[\s/>])((?:[^>""]|""[^""]*"")*?)/?>";

            foreach (Match m in Regex.Matches(text, pattern, RegexOptions.Singleline))
            {
                var tag = m.Groups[1].Value;
                var window = text[m.Index..Math.Min(text.Length, m.Index + m.Length + 600)];

                yield return new Control(
                    Path.GetFileName(path),
                    text[..m.Index].Count(c => c == '\n') + 1,
                    tag,
                    m.Groups[2].Value,
                    window.Contains("<" + tag + ".Flyout", StringComparison.Ordinal)
                    || window.Contains("<" + tag + ".ContextMenu", StringComparison.Ordinal)
                    || OpensASubmenu(tag, text, m.Index + m.Length),
                    m.Value.EndsWith("/>", StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    /// A <c>MenuItem</c> whose first child is another <c>MenuItem</c> is a submenu header,
    /// and opening its own children is the whole of its job — the same case as a control
    /// hosting a <c>&lt;Button.Flyout&gt;</c>. Without this, the separator's "Colour ▸"
    /// header reads as a dead control, which it is not.
    /// <para>
    /// Deliberately narrow: the FIRST thing inside the element, not "a MenuItem somewhere
    /// nearby". A 600-character window would happily excuse the next sibling too, and a
    /// guard that excuses siblings is how a genuinely dead row hides behind a live one.
    /// </para>
    /// </summary>
    private static bool OpensASubmenu(string tag, string markup, int contentStart)
    {
        if (tag != "MenuItem" || contentStart >= markup.Length) return false;

        var rest = markup[contentStart..].TrimStart();
        while (rest.StartsWith("<!--", StringComparison.Ordinal))
        {
            var end = rest.IndexOf("-->", StringComparison.Ordinal);
            if (end < 0) return false;
            rest = rest[(end + 3)..].TrimStart();
        }

        return rest.StartsWith("<MenuItem", StringComparison.Ordinal);
    }

    /// <summary>
    /// A disabled control must say what it is waiting for.
    /// <para>
    /// The project's rule is that an unbuilt action renders disabled rather than hidden,
    /// because a greyed row teaches the product and a missing one misrepresents it. That
    /// only holds if it explains itself, and four were disabled AND silent —
    /// "Activate all", "Collapse all", "Preview what will be written…" and "Revert to
    /// last applied" — which reads as broken rather than as not built.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_permanently_disabled_control_says_what_it_is_waiting_for()
    {
        var offenders = Controls()
            .Where(c => c.Attrs.Contains("IsEnabled=\"False\"", StringComparison.Ordinal))
            // A context menu's header row is a label, not a control: disabled so it
            // cannot be invoked, and it already states its own subject.
            .Where(c => !c.Attrs.Contains("RowContext.Header"))
            // First-run's crash-reporting consent renders disabled with its reason as
            // body text beside it, which is the point of that card.
            .Where(c => !c.Attrs.Contains("Classes=\"rowCheck\""))
            .Where(c => !c.Attrs.Contains("ToolTip.Tip"))
            .Select(c => $"{c.File}:{c.Line} <{c.Tag}>")
            .ToList();

        offenders.Should().BeEmpty(
            "a disabled control with no explanation reads as broken; the rule is that an "
            + "unbuilt action renders disabled AND says so");
    }

    /// <summary>
    /// A control that looks live must do something — a command, a click handler, a
    /// two-way check, a flyout to open, a name the code-behind wires it by, or items to
    /// expand. Anything else renders, highlights under the pointer, accepts a click and
    /// answers with nothing.
    /// </summary>
    [Fact]
    public void Every_enabled_control_does_something()
    {
        string[] wiring =
        [
            "Command=", "Click=", "Tapped=", "IsChecked=", "x:Name=",
            "ItemsSource=", "ToggleType=", "IsEnabled=\"False\"",
            "Flyout",
            // Deliberately inert: the Apply flyout's pending-diff footer line.
            "IsHitTestVisible=\"False\"",
            // Enablement decided by DATA rather than markup. Allowed only because
            // WarningsPresenterTests pins the other half: every WarningAction is
            // constructed disabled, so the day one is enabled that test fails and
            // names the command it still needs.
            "IsEnabled=\"{Binding",
        ];

        var offenders = Controls()
            .Where(c => !c.Opens)
            .Where(c => !wiring.Any(w => c.Attrs.Contains(w, StringComparison.Ordinal)))
            .Select(c => $"{c.File}:{c.Line} <{c.Tag}>")
            .ToList();

        offenders.Should().BeEmpty(
            "a control with no command, click, binding, name or flyout renders and "
            + "answers a click with nothing — the defect that has outlived four phases here");
    }

    /// <summary>
    /// A control that DOES something must also SAY what it does.
    /// <para>
    /// The sibling test above asks whether a control is wired, and accepts
    /// <c>x:Name=</c> as proof. Both pane-footer buttons had a name, a tooltip and a
    /// working click handler — and no content whatsoever, so they rendered as two empty
    /// squares beside "Activate all" and "+ Separator" for the entire life of the
    /// footers. Every guard passed, because none of them asked what the user could see.
    /// </para>
    /// <para>
    /// Only SELF-CLOSING controls are judged: anything with children may be showing an
    /// icon, a StackPanel or a templated row, and deciding legibility from markup text
    /// there would be guesswork. A self-closing control shows exactly what its
    /// attributes say, so the rule is decidable rather than a judgement call — the same
    /// standard the horizontal-StackPanel trap is stated at.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_self_closing_control_says_what_it_is()
    {
        string[] speaks =
        [
            "Content=", "Header=", "Text=",
            // An icon-only control is legible if it names itself for assistive tech and
            // says what it does on hover; the toolbar's ⋯ is the shape this allows.
            "AutomationProperties.Name=",
        ];

        var offenders = Controls()
            .Where(c => c.SelfClosing)
            .Where(c => !speaks.Any(s => c.Attrs.Contains(s, StringComparison.Ordinal)))
            .Select(c => $"{c.File}:{c.Line} <{c.Tag}>")
            .ToList();

        offenders.Should().BeEmpty(
            "a self-closing control with no Content, Header, Text or AutomationProperties"
            + ".Name renders as a blank box — it works, it just never says so");
    }
}
