using System.IO;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The modlist switcher is the only way to change which list is open, so a binding that
/// silently resolves to nothing makes the whole feature unreachable. This control has
/// already shipped dead once — a chevron, a tooltip and no command behind either, for
/// four phases.
/// </summary>
public sealed class ModlistSwitcherMarkupTests
{
    private static string Toolbar => File.ReadAllText(
        Path.Combine(RepoPaths.AppProject, "Views", "Shell", "ToolbarView.axaml"));

    [Fact]
    public void The_toolbar_selector_switches_modlists_and_not_instances()
    {
        var markup = Toolbar;

        markup.Should().Contain("{Binding SelectedModlistName}",
            "the selector's label is what says which list is open");
        markup.Should().Contain("{Binding ModlistChoices}",
            "without the flyout's source the button opens an empty menu");
        markup.Should().NotContain("InstanceChoices",
            "instances are gone; a leftover binding would populate a menu that switches nothing");
    }

    /// <summary>
    /// A MenuFlyout's content lives in a popup, so an ancestor binding reaching for
    /// $parent[Window] resolves against nothing and fails in silence — every entry would
    /// highlight and do nothing. The command has to be on the row.
    /// </summary>
    [Fact]
    public void Each_switcher_row_carries_its_own_command()
    {
        var markup = Toolbar;

        // Scoped to the switcher's own themes. $parent[Window] is legitimate elsewhere on
        // this toolbar — it is only fatal inside a popup, which is where these live.
        foreach (var region in Regions(markup, "{Binding ModlistChoices}", "</ControlTheme>"))
        {
            region.Should().Contain("Value=\"{Binding SelectCommand}\"",
                "the row's own command is the only one a popup can reach");
            region.Should().NotContain("$parent[",
                "an ancestor binding cannot escape a popup and fails in silence");
        }
    }

    /// <summary>Every span from a marker up to the following terminator.</summary>
    private static IEnumerable<string> Regions(string text, string marker, string terminator)
    {
        var found = false;
        var from = 0;

        while ((from = text.IndexOf(marker, from, System.StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf(terminator, from, System.StringComparison.Ordinal);
            if (end < 0) break;

            found = true;
            yield return text[from..end];
            from = end;
        }

        found.Should().BeTrue($"the markup should still contain '{marker}'");
    }

    /// <summary>
    /// Below 900px the selector is off the bar entirely, so the overflow menu is the only
    /// route to another list. Losing it there would strand the user on one modlist.
    /// </summary>
    [Fact]
    public void The_narrow_layout_keeps_a_route_to_the_switcher()
    {
        Toolbar.Should().Contain("Header=\"Modlist\"",
            "the overflow menu is the only switcher below 900px");
    }

    /// <summary>
    /// "Switch instance" there was dead logic once instances went: every record held the
    /// same paths, so switching could never fix a missing game folder — and a modlist says
    /// which mods, never where the game is.
    /// </summary>
    [Fact]
    public void The_game_not_found_state_offers_settings_rather_than_a_switcher()
    {
        var markup = File.ReadAllText(
            Path.Combine(RepoPaths.AppProject, "Views", "Shell", "GameMissingView.axaml"));

        markup.Should().Contain("OpenSettingsCommand",
            "Settings is the only route that actually fixes a wrong path");
        markup.Should().NotContain("InstanceChoices");

        // The CONTROL, not the words — the comment above it explains why the button was
        // removed and legitimately names the thing it replaced.
        markup.Should().NotContain("Content=\"Switch instance\"");
    }
}
