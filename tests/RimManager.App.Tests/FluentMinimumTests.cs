using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// A declared <c>Width</c>/<c>Height</c> below the control's Fluent template minimum
/// is silently ignored: the final size is the declared value CLAMPED BY
/// <c>MinWidth</c>/<c>MinHeight</c>, so the minimum wins and nothing reports it.
/// <para>
/// Four bugs so far — <c>CheckBox</c>, <c>ToggleSwitch</c>, the UI-scale <c>Slider</c>,
/// and the status bar's progress bar, which declared 60x2, rendered 200x4 and took its
/// space from the one flexible zone in the band. Every one was found by eye, months
/// apart. The minimums in <see cref="Minimums"/> were then MEASURED from the running
/// app across the main window, all five dock tabs and all seven settings pages.
/// </para>
/// </summary>
public sealed class FluentMinimumTests
{
    /// <summary>
    /// Control types whose template carries a floor, and what it is. Only types we
    /// actually declare explicit sizes on need to be here.
    /// </summary>
    private static readonly (string Type, double MinWidth, double MinHeight)[] Minimums =
    [
        ("ProgressBar", 200, 4),
        ("Slider", 240, 0),
        ("ComboBox", 190, 32),
        ("TextBox", 64, 0),
    ];

    [Fact]
    public void A_size_below_the_template_minimum_also_switches_the_minimum_off()
    {
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(path);

            foreach (var (type, minWidth, minHeight) in Minimums)
            {
                foreach (Match tag in Regex.Matches(markup, $@"<{type}\b[^>]*>", RegexOptions.Singleline))
                {
                    Check(tag.Value, "Width", minWidth);
                    Check(tag.Value, "Height", minHeight);

                    void Check(string element, string axis, double floor)
                    {
                        if (floor <= 0) return;

                        var declared = Regex.Match(element, $@"\b{axis}=""([\d.]+)""");
                        if (!declared.Success) return;
                        if (double.Parse(declared.Groups[1].Value) >= floor) return;

                        // Asked for less than the floor — so the floor has to be off,
                        // or the declaration is a comment rather than a size.
                        if (Regex.IsMatch(element, $@"\bMin{axis}=""0""")) return;

                        offenders.Add(
                            $"{Path.GetFileName(path)}: <{type} {axis}={declared.Groups[1].Value}> " +
                            $"is below Fluent's Min{axis}={floor} and does not set Min{axis}=\"0\"");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "Width/Height are clamped BY MinWidth/MinHeight, so a size below the " +
            "template's floor is silently ignored — see the FLUENT MINIMUMS table in " +
            "Themes/Controls.axaml");
    }

    /// <summary>
    /// The table in <c>Controls.axaml</c> is the only written record of these figures,
    /// and it is what sends the next person to <c>MinWidth="0"</c> instead of to a
    /// screenshot. Pin that it still says so.
    /// </summary>
    [Fact]
    public void The_measured_table_stays_in_the_theme_where_sizes_are_written()
    {
        var controls = File.ReadAllText(Path.Combine(RepoPaths.Themes, "Controls.axaml"));

        controls.Should().Contain("FLUENT MINIMUMS");
        foreach (var (type, minWidth, _) in Minimums)
            controls.Should().Contain($"{type}", $"the table must still list {type}'s floor of {minWidth}");
    }

    // --- scrollbars (UI-9) ----------------------------------------------------

    private static string Controls =>
        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Themes", "Controls.axaml"));

    /// <summary>
    /// ScrollBar carries MinWidth 16 AND MinHeight 16, so a declared thickness alone
    /// does nothing — the bar stays 16px and looks exactly like a style that worked.
    /// Both have to be reset, and a vertical bar needs the height reset as much as the
    /// width because the same style block serves both orientations.
    /// </summary>
    [Fact]
    public void The_scrollbar_resets_both_template_minimums()
    {
        var block = Section(Controls, "<Style Selector=\"ScrollBar\">");

        block.Should().Contain("Property=\"MinWidth\" Value=\"0\"");
        block.Should().Contain("Property=\"MinHeight\" Value=\"0\"");
    }

    /// <summary>
    /// The thumb is sized with MaxWidth/MaxHeight, never Width/Height.
    /// <para>
    /// Measured on the running app: setting Background on the thumb selector works and
    /// setting a size on it does not — the thumb stayed 16px wide inside a 10px bar.
    /// Fluent's template gives it a size as a LOCAL value, and a local value outranks
    /// every style setter. MaxWidth is a different property, so it clamps regardless.
    /// </para>
    /// </summary>
    [Fact]
    public void The_scrollbar_thumb_is_clamped_rather_than_sized()
    {
        var offenders = new List<string>();

        foreach (Match style in Regex.Matches(
                     Controls, @"<Style Selector=""[^""]*ScrollBar[^""]*Thumb[^""]*"">(.*?)</Style>",
                     RegexOptions.Singleline))
        {
            if (Regex.IsMatch(style.Groups[1].Value, @"Property=""(Width|Height)"""))
                offenders.Add(style.Value[..style.Value.IndexOf('>')].Trim());
        }

        offenders.Should().BeEmpty(
            "Fluent sets the thumb's size locally, so a Width/Height setter is ignored; "
            + "MaxWidth/MaxHeight clamp it instead");
    }

    /// <summary>
    /// The arrow buttons go by NAME, and all four names are needed: a ScrollBar template
    /// carries an up/down pair and a left/right pair, and hiding only one orientation
    /// leaves stubs on the other.
    /// </summary>
    [Theory]
    [InlineData("PART_LineUpButton")]
    [InlineData("PART_LineDownButton")]
    [InlineData("PART_LineLeftButton")]
    [InlineData("PART_LineRightButton")]
    public void Every_scrollbar_arrow_is_hidden(string part)
    {
        Controls.Should().Contain(part);
    }

    /// <summary>The first Style block whose selector matches, body included.</summary>
    private static string Section(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;

        var end = text.IndexOf("</Style>", start, StringComparison.Ordinal);
        return end < 0 ? string.Empty : text[start..end];
    }
}
