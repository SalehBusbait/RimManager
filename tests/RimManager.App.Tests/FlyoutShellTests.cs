using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The one flyout shell every dropdown wears (N3 · UI-8), and the two ways it can stop
/// applying without anything failing.
/// </summary>
public sealed class FlyoutShellTests
{
    private static string Controls =>
        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Themes", "Controls.axaml"));

    /// <summary>
    /// Fluent paints a MenuItem's hover and selected fill on the inner Border named
    /// <c>PART_LayoutRoot</c>, and a value set on an element beats anything inherited
    /// from its parent. Styling <c>MenuItem</c>'s own Background therefore changes
    /// nothing at all — and looks exactly like a style that works.
    /// <para>
    /// Measured before the shell existed: the item was Transparent while its
    /// PART_LayoutRoot carried <c>#19FFFFFF</c>. This is the fourth control whose state
    /// colours have had to be taken over this way, after CheckBox, ToggleSwitch and
    /// Button.accent.
    /// </para>
    /// </summary>
    [Fact]
    public void Menu_item_state_colours_are_set_on_the_part_that_actually_paints_them()
    {
        var offenders = new List<string>();

        foreach (Match style in Regex.Matches(
                     Controls, @"<Style Selector=""([^""]*MenuItem[^""]*)"">(.*?)</Style>",
                     RegexOptions.Singleline))
        {
            var selector = style.Groups[1].Value;
            var body = style.Groups[2].Value;

            var isStateStyle = selector.Contains(":pointerover") || selector.Contains(":selected");
            if (!isStateStyle) continue;
            if (!body.Contains("Property=\"Background\"")) continue;

            if (!selector.Contains("PART_LayoutRoot")) offenders.Add(selector);
        }

        offenders.Should().BeEmpty(
            "Fluent paints MenuItem state fills on Border#PART_LayoutRoot, so a state "
            + "Background set on the MenuItem itself is overridden and does nothing");
    }

    /// <summary>
    /// The shell is written ONCE, as type selectors. A dropdown that sets its own
    /// background or border in markup silently opts out — a local value outranks every
    /// style setter — and the app grows a second look nobody notices, because only one
    /// dropdown is on screen at a time.
    /// </summary>
    [Fact]
    public void No_dropdown_restyles_itself_in_markup()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (Path.GetFileName(file) == "Controls.axaml") continue;   // the shell itself

            foreach (Match element in Regex.Matches(
                         File.ReadAllText(file),
                         @"<(MenuFlyout|ContextMenu|Flyout|FlyoutPresenter|MenuFlyoutPresenter)\b[^>]*>",
                         RegexOptions.Singleline))
            {
                if (Regex.IsMatch(element.Value, @"\b(Background|BorderBrush|BorderThickness|CornerRadius)="))
                    offenders.Add($"{Path.GetFileName(file)}: {element.Value.Split('\n')[0].Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            "the flyout shell in Controls.axaml owns how every dropdown looks; a local "
            + "value here outranks it and creates a second look that drifts");
    }

    /// <summary>
    /// The shell has to reach all four surfaces. They are different control types —
    /// MenuFlyout renders a MenuFlyoutPresenter, a plain Flyout renders a
    /// FlyoutPresenter, and a ContextMenu is its own MenuBase — so styling one and
    /// assuming the rest follow leaves dropdowns in Fluent's grey.
    /// </summary>
    [Theory]
    [InlineData("MenuFlyoutPresenter")]
    [InlineData("FlyoutPresenter")]
    [InlineData("ContextMenu")]
    public void Every_dropdown_surface_type_is_given_the_shell(string type)
    {
        Controls.Should().MatchRegex(
            $@"<Style Selector=""{type}"">",
            $"{type} is a distinct control type and does not inherit another's styling");
    }

    /// <summary>
    /// The presenter's own template wraps its content in a Border named LayoutRoot, and
    /// that Border carries the background and border — so the presenter's setters alone
    /// leave Fluent's black frame drawn on top of our shell.
    /// </summary>
    [Fact]
    public void The_presenters_inner_border_is_styled_too_not_just_the_presenter()
    {
        Controls.Should().Contain("MenuFlyoutPresenter /template/ Border#LayoutRoot",
            "the presenter's inner Border paints the frame; setting only the presenter "
            + "leaves Fluent's black border visible");
    }
}
