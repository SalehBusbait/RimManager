using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Guards for the two adaptive-layout defects the R9 hand review found. Both built
/// clean, passed 919 tests and were invisible at the default window size — which is
/// the whole reason they are pinned as source checks rather than trusted to review.
/// </summary>
public sealed class AdaptiveLayoutMarkupTests
{
    /// <summary>
    /// The toolbar's code-behind moves and caps columns <b>by index</b>, and the markup
    /// numbers them by hand. Inserting a control shifts every index after it, and nothing
    /// in the compiler or the tests connects the two — a stale constant would move Apply
    /// into the search field's column at the breakpoint, or cap the wrong column, and both
    /// are invisible until someone drags the window narrow.
    /// <para>
    /// Found the need for this by inserting Refresh at column 3 and having to renumber
    /// eight siblings plus three constants by hand.
    /// </para>
    /// </summary>
    [Fact]
    public void The_toolbars_column_constants_match_its_markup()
    {
        var markup = File.ReadAllText(
            Path.Combine(RepoPaths.AppProject, "Views", "Shell", "ToolbarView.axaml"));
        var code = File.ReadAllText(
            Path.Combine(RepoPaths.AppProject, "Views", "Shell", "ToolbarView.axaml.cs"));

        int Constant(string name) =>
            int.Parse(Regex.Match(code, $@"{name}\s*=\s*(\d+)").Groups[1].Value);

        // The named direct children — the only ones the code-behind addresses.
        int ColumnOf(string name) =>
            int.Parse(Regex.Match(markup, $@"Grid\.Column=""(\d+)""[^>]*?x:Name=""{name}""")
                .Groups[1].Value);

        ColumnOf("ApplyButton").Should().Be(Constant("ApplyWideColumn"),
            "the code-behind puts Apply back here when the window widens");

        // The search column is the one * column, and the cap is set on it by index.
        var columns = Regex.Matches(markup, @"<ColumnDefinition Width=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToList();

        columns.IndexOf("*").Should().Be(Constant("SearchColumn"),
            "SearchWideCap is applied to this index, and capping the wrong column strands "
            + "the filter chips at the far right on a wide window");

        Constant("ApplyNarrowColumn").Should().Be(columns.Count - 1,
            "below 900px Apply moves to the last column so it sits at the right edge");
    }

    /// <summary>
    /// Every surface must decide its breakpoint from <c>LayoutWidth</c>, never from
    /// <c>WindowWidth</c>.
    /// <para>
    /// They are the same number only at 100% UI scale. <c>LayoutWidth</c> is
    /// <c>WindowWidth ÷ scale</c> — the width the layout actually gets — and R9i moved
    /// the window, the menu bar and the row columns onto it. <c>ToolbarView</c> was
    /// missed and kept reading <c>WindowWidth</c>, so at 150% on an 1180px window the
    /// lists went segmented while the toolbar stayed in its full arrangement: the
    /// segmented switch, which is the only control that reaches the other list, was
    /// hidden, and the inactive pane could not be got to at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Breakpoints_are_decided_from_the_layout_width_never_the_window_width()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            // The hub is where LayoutWidth is DERIVED from WindowWidth, so it is the
            // one CLASS allowed to mention both in one breath — several files since
            // the N11 partial split.
            if (Path.GetFileName(file).StartsWith("MainWindowViewModel")) continue;

            var text = File.ReadAllText(file);
            foreach (Match call in Regex.Matches(text, @"Breakpoints\.\w+\([^)]*\)"))
            {
                if (call.Value.Contains("WindowWidth"))
                    offenders.Add($"{Path.GetFileName(file)}: {call.Value}");
            }
        }

        offenders.Should().BeEmpty(
            "a breakpoint read from WindowWidth disagrees with the rest of the app at " +
            "every UI scale except 100%; use LayoutWidth");
    }

    /// <summary>
    /// A column header is a legend for the rows beneath it, so its column definitions
    /// must match its row template's exactly — for <b>both</b> panes.
    /// <para>
    /// They were literal on both sides until R9, and then the active row template grew
    /// a sixteenth column — the info chevron, 14px wide whenever mod info is an
    /// overlay. The header never got it, and the header is on screen between 900 and
    /// 1150px, so in that band every heading from PACKAGEID rightwards sat 14px off the
    /// column it names. Nothing threw; the numbers were simply written down twice.
    /// </para>
    /// <para>
    /// The inactive pane joined this test when it gained a header in N1, and it needs
    /// the check MORE than the active pane does: its widths are computed from the
    /// pane's own measured width rather than fixed, so a header column bound to the
    /// wrong property would move as the splitter is dragged, which reads as a rendering
    /// glitch rather than as a mismatch.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("ACTIVE LOAD ORDER", "ModRowTemplate")]
    [InlineData("INACTIVE", "InactiveModRowTemplate")]
    public void A_column_header_matches_its_row_template_column_for_column(
        string pane, string template)
    {
        var window = File.ReadAllText(Path.Combine(RepoPaths.AppProject, "MainWindow.axaml"));

        // Anchored on the pane's own caption, because there are TWO columnHeader bands
        // now and searching for the class alone found whichever came first in the file
        // — which silently compared the inactive header against the active rows.
        var header = ColumnWidths(Section(window, after: $"Text=\"{pane}\""));

        var row = ColumnWidths(
            Section(File.ReadAllText(Path.Combine(
                RepoPaths.AppProject, "Views", "Mods", "ModRowTemplates.axaml")),
                after: $"x:Key=\"{template}\""));

        header.Should().NotBeEmpty($"{pane}'s header ColumnDefinitions block was not found");
        row.Should().NotBeEmpty($"{template}'s ColumnDefinitions block was not found");

        header.Should().Equal(row,
            "the header is a legend for these exact columns — a column in one and not " +
            "the other slides every heading to its right off the data it names");
    }

    /// <summary>
    /// Both headers must be anchored on a <c>columnHeader</c> band, or the test above
    /// would happily compare some other grid that happens to follow the caption.
    /// </summary>
    [Fact]
    public void Both_panes_carry_a_column_header_band()
    {
        var window = File.ReadAllText(Path.Combine(RepoPaths.AppProject, "MainWindow.axaml"));

        Regex.Matches(window, "Classes=\"columnHeader\"").Count.Should().Be(2,
            "one legend per list — the active pane's, and the inactive pane's sortable one");
    }

    /// <summary>The first ColumnDefinitions block after a marker.</summary>
    private static string Section(string markup, string after)
    {
        var start = markup.IndexOf(after, StringComparison.Ordinal);
        if (start < 0) return string.Empty;

        var open = markup.IndexOf("<Grid.ColumnDefinitions>", start, StringComparison.Ordinal);
        if (open < 0) return string.Empty;

        var close = markup.IndexOf("</Grid.ColumnDefinitions>", open, StringComparison.Ordinal);
        return close < 0 ? string.Empty : markup[open..close];
    }

    /// <summary>
    /// Each column's width as a comparable token. A binding is reduced to the property
    /// it targets, because the header binds it directly and the row template has to
    /// reach the window's DataContext for it — same source, different path.
    /// </summary>
    private static List<string> ColumnWidths(string section) =>
        [.. Regex.Matches(section, @"Width=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Select(w => w.StartsWith('{')
                ? Regex.Match(w, @"(\w+)\s*}$").Groups[1].Value
                : w)];
}
