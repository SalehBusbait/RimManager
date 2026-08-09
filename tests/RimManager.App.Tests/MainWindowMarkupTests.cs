using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Markup presence guards for bindings that carry behaviour no unit test can reach.
/// <para>
/// These exist because of a real regression: a bulk edit to MainWindow.axaml removed
/// <c>Window.Styles</c> and <c>Window.KeyBindings</c> wholesale. Everything still
/// built, all tests stayed green and the app launched clean — but rows stopped
/// hiding when filtered and Ctrl+Z stopped working, for four commits, until it was
/// spotted by eye in a screenshot.
/// </para>
/// <para>
/// Asserting on markup is crude. It is worth it here because the alternative is a
/// class of silent, invisible breakage that only a human looking at the window can
/// catch, and because these particular bindings are load-bearing.
/// </para>
/// </summary>
public sealed class MainWindowMarkupTests
{
    private static string MainWindow =>
        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "MainWindow.axaml"));

    /// <summary>
    /// Search and the filter chips set IsFilteredOut on each row; THIS binding is
    /// what turns that into a hidden row. Without it the lists stay full while the
    /// empty state truthfully reports "nothing matches".
    /// </summary>
    [Fact]
    public void Rows_bind_their_visibility_so_filtering_actually_hides_them()
    {
        var markup = MainWindow;

        markup.Should().Contain("<Window.Styles>",
            "the ListBoxItem style lives here and carries the row-visibility binding");
        markup.Should().Contain("IsRowVisible",
            "filtering computes IsRowVisible; nothing else applies it to the row");
    }

    /// <summary>
    /// The key bindings are GENERATED, so the markup must not grow a hand-written set
    /// alongside them.
    /// <para>
    /// Eleven were written out by hand against a table of forty-six, and because
    /// <c>MenuItem.InputGesture</c> only draws a gesture, seven shortcuts printed
    /// themselves in the menus and did nothing when pressed. A second, partial list in
    /// markup would silently re-create that gap — and would also double-bind whatever
    /// it duplicated.
    /// </para>
    /// </summary>
    [Fact]
    public void The_window_does_not_hand_write_key_bindings_beside_the_generated_ones()
    {
        MainWindow.Should().NotContain("<Window.KeyBindings>",
            "they are generated from ShortcutTable in InstallKeyBindings");

        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "MainWindow.axaml.cs"))
            .Should().Contain("ShortcutBindings.For(vm.CommandFor)",
                "one table feeds the menus, the palette, the sheet AND the bindings");
    }

    /// <summary>
    /// The dock's height grip only works if the strip and the body share one grid
    /// row: a GridSplitter resizes the rows either side of itself, and the strip has
    /// to stay visible when the dock is closed. Lose the two-way DockRow binding and
    /// the grip silently stops resizing anything — no error, no failing test.
    /// </summary>
    [Fact]
    public void The_dock_region_row_is_two_way_bound_so_the_grip_resizes_it()
    {
        var markup = MainWindow;

        markup.Should().Contain("{Binding DockRow, Mode=TwoWay}",
            "the grip writes the new row height back through this binding");
        markup.Should().Contain("Classes=\"dockHeight\"",
            "the grip itself");
    }

    /// <summary>
    /// SCREENS.md: "All six tabs are the same shell. Build it once." A tab that
    /// hand-rolls its own DockPanel drifts from the other five in band heights and
    /// splitter behaviour, and nothing catches it.
    /// </summary>
    [Fact]
    public void Dock_tabs_are_built_on_the_shared_shell()
    {
        MainWindow.Should().Contain("<dock:DockTabShell",
            "the dock skeleton is one control, not six copies");
    }

    /// <summary>
    /// The drag ghost (3a §1) lives on a window-level Canvas because it crosses both
    /// panes and the gap between them. Lose the layer and the ghost silently stops
    /// appearing — the drag still works, so nothing fails.
    /// </summary>
    [Fact]
    public void The_drag_ghost_has_a_layer_to_live_in()
    {
        var markup = MainWindow;

        markup.Should().Contain("x:Name=\"DragGhostLayer\"");
        markup.Should().Contain("IsHitTestVisible=\"False\"",
            "the ghost must not eat the drop it is describing");
    }

    /// <summary>
    /// The Tags chip is lit by the FILTER, never by the click that opens its flyout.
    /// As a ToggleButton with a stored bool it lit itself on open for four phases
    /// while filtering nothing (N4g) — the binding below is what makes the light a
    /// report instead of a claim, and a revert to IsChecked would rebuild the lie.
    /// </summary>
    [Fact]
    public void The_tags_chip_reports_the_running_filter_not_its_own_click()
    {
        var toolbar = File.ReadAllText(Path.Combine(
            RepoPaths.AppProject, "Views", "Shell", "ToolbarView.axaml"));

        toolbar.Should().Contain("Classes.on=\"{Binding HasTagFilter}\"",
            "the chip's lit state is computed from the selection the filter runs on");
        toolbar.Should().NotContain("IsChecked=\"{Binding HasTagFilter}\"",
            "a toggle lights on the very click that opens the flyout");
    }

    /// <summary>
    /// A ControlTheme is keyed by type and resolved from the ancestor resource chain.
    /// If its dictionary is not merged at application level the shell renders as an
    /// empty box — no exception, no failing build, no failing test. This is the same
    /// silent-styling class of bug as the menu headers and the drop indicator.
    /// </summary>
    [Fact]
    public void The_dock_shell_theme_is_declared_and_merged_at_application_level()
    {
        var theme = File.ReadAllText(
            Path.Combine(RepoPaths.AppProject, "Views", "Dock", "DockShell.axaml"));
        theme.Should().Contain("x:Key=\"{x:Type dock:DockTabShell}\"",
            "a ControlTheme is found by its target type, not by name");

        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "App.axaml"))
            .Should().Contain("Views/Dock/DockShell.axaml",
                "merged at application level, or a shell inside a UserControl never finds it");
    }

    /// <summary>
    /// A local value on an element outranks every style setter, so an element that
    /// sets a property locally can never be styled on that property by a bound class.
    /// <para>
    /// This is the third time the trap has bitten: menu rows rendered as their type
    /// name because <c>Menu.ItemsSource</c> assigns <c>Header</c> locally; the drop
    /// indicator drew as bare text; and the tag stripe was invisible for two phases
    /// because the row template set <c>Background="Transparent"</c> on the very Border
    /// whose background <c>Border.paletted.p0..p5</c> exists to set. All three built
    /// clean, tested green and launched fine.
    /// </para>
    /// </summary>
    [Fact]
    public void No_element_sets_a_property_locally_that_its_bound_classes_style()
    {
        // class prefix -> the property those classes set
        var styled = new Dictionary<string, string>
        {
            ["Classes.p"] = "Background",        // Border.paletted.p0..p5
            ["Classes.src"] = "Background",      // Border.srcBadge.src*
        };

        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (var element in Elements(File.ReadAllText(file)))
            {
                foreach (var (prefix, property) in styled)
                {
                    if (!element.Contains(prefix, StringComparison.Ordinal)) continue;

                    element.Should().NotContain($"{property}=\"",
                        $"{Path.GetFileName(file)} — a local {property} outranks the style "
                        + $"that {prefix}* exists to apply, so the class would do nothing");
                }
            }
        }
    }

    /// <summary>
    /// The source badge's tint must be able to REACH the element it colours.
    /// <para>
    /// N1 replaced the badge's single letter with one of six icons, and the icons sit
    /// inside a <c>Panel</c> so exactly one can be shown at a time. Both halves of that
    /// can silently stop working, and neither throws:
    /// </para>
    /// <list type="number">
    ///   <item>the selector still naming <c>TextBlock</c>, the element that went; or</item>
    ///   <item>the selector using <c>&gt;</c>, which reaches the Panel and stops — the
    ///   icons are its grandchildren, not its children.</item>
    /// </list>
    /// <para>
    /// Either way every badge in both lists renders in the default foreground: six
    /// tinted backgrounds carrying six identically-coloured marks. That is precisely
    /// the class of defect this project keeps shipping, so it is pinned in source.
    /// </para>
    /// </summary>
    [Fact]
    public void The_source_badge_tint_reaches_the_icon_inside_it()
    {
        var template = File.ReadAllText(Path.Combine(
            RepoPaths.AppProject, "Views", "Mods", "ModRowTemplates.axaml"));
        var controls = File.ReadAllText(Path.Combine(
            RepoPaths.AppProject, "Themes", "Controls.axaml"));

        var badge = Between(template, "x:Key=\"SourceBadgeTemplate\"", "</DataTemplate>");
        badge.Should().NotBeEmpty("the shared source-badge template was not found");

        // One icon per source, and no more: a sixth would render on top of another.
        // Five since O13 took Pinned with the vault.
        Regex.Matches(badge, "<PathIcon").Count.Should().Be(5,
            "the badge shows one of five sources, so it holds five icons and picks by IsVisible");

        badge.Should().NotContain("Foreground=",
            "a local Foreground on an icon outranks the tint style keyed off its "
            + "bound class, which is the trap that has cost this project three bugs");

        foreach (var source in new[] { "srcCore", "srcDlc", "srcWorkshop", "srcLocal", "srcGit" })
        {
            var selector = $"Border.srcBadge.{source} PathIcon";

            controls.Should().Contain(selector,
                $"{source} needs a DESCENDANT tint selector onto PathIcon — the icons "
                + "are nested in a Panel, so a '>' combinator stops at the Panel, and "
                + "the element it used to name (TextBlock) no longer exists");
        }
    }

    /// <summary>The text between two markers, exclusive.</summary>
    private static string Between(string text, string after, string before)
    {
        var start = text.IndexOf(after, StringComparison.Ordinal);
        if (start < 0) return string.Empty;

        var end = text.IndexOf(before, start, StringComparison.Ordinal);
        return end < 0 ? string.Empty : text[start..end];
    }

    /// <summary>
    /// A <c>TextBlock</c> narrowed by <c>MaxWidth</c> must say where it sits.
    /// <para>
    /// The default <c>HorizontalAlignment</c> is Stretch, and the layout system CENTRES a
    /// stretched element that a MaxWidth has made narrower than its slot. Every
    /// explanatory note on every Settings page therefore rendered as a floating block
    /// half-way across the pane, nowhere near the control it explained. Nothing threw,
    /// nothing failed, and it shipped through three page reviews before a screenshot
    /// caught it — the same silent-styling family as the tag stripe and the menu headers.
    /// </para>
    /// <para>
    /// <c>TextBlock.note</c> sets Left for exactly this reason; anything else has to be
    /// explicit about it.
    /// </para>
    /// </summary>
    [Fact]
    public void No_TextBlock_is_narrowed_by_MaxWidth_without_saying_where_it_sits()
    {
        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (var element in Elements(File.ReadAllText(file)))
            {
                if (!element.StartsWith("<TextBlock", StringComparison.Ordinal)) continue;
                if (!element.Contains("MaxWidth=", StringComparison.Ordinal)) continue;

                var explicitlyPlaced =
                    element.Contains("HorizontalAlignment=", StringComparison.Ordinal)
                    || element.Contains("TextAlignment=", StringComparison.Ordinal);

                explicitlyPlaced.Should().BeTrue(
                    $"{Path.GetFileName(file)} — a MaxWidth'd TextBlock with the default "
                    + "Stretch alignment is centred by the layout system. Use Classes=\"note\" "
                    + $"or state the alignment. Offending element: {element}");
            }
        }
    }

    /// <summary>
    /// 2k's first-scan state covers the three panes and only the three panes: mounted
    /// anywhere else it would either blank the toolbar and dock (breaking the promise
    /// it makes on screen) or never appear at all. It has to be the LAST child of the
    /// pane grid, since siblings in a Grid stack in declaration order.
    /// </summary>
    [Fact]
    public void The_first_scan_state_covers_the_panes_and_is_bound_to_the_scan()
    {
        MainWindow.Should().Contain("<shell:ScanStateView",
            "the state is mounted over the pane grid");

        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Views", "Shell", "ScanStateView.axaml"))
            .Should().Contain("IsVisible=\"{Binding IsScanning}\"",
                "nothing else raises or lowers it");
    }

    /// <summary>
    /// 2k's game-not-found state is still mounted over the panes.
    /// <para>
    /// This test used to also pin an INSTANCE switcher in both this state and the
    /// toolbar. Instances are gone (the modlist migration) and the two surfaces
    /// diverged for a real reason rather than a cosmetic one: every instance record held
    /// the same paths, so "switch instance" could never fix a missing game folder, and a
    /// modlist says which mods and never where the game is. The state offers Settings
    /// now. What the switcher must still do lives in
    /// <see cref="ModlistSwitcherMarkupTests"/>, including the popup rule this test was
    /// originally written for.
    /// </para>
    /// </summary>
    [Fact]
    public void The_game_not_found_state_is_mounted_over_the_panes()
    {
        MainWindow.Should().Contain("<shell:GameMissingView",
            "the state is mounted over the pane grid");

        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Views", "Shell", "GameMissingView.axaml"))
            .Should().Contain("IsVisible=\"{Binding IsGameMissing}\"",
                "nothing else raises or lowers it");
    }

    /// <summary>
    /// 2k · offline is per feature and never global. The three places it shows are the
    /// strip, the status bar's rules zone and the Updates tab's stale badge; if any of
    /// them silently stops binding, the app looks like it is working normally while
    /// showing a result it can no longer vouch for.
    /// </summary>
    [Fact]
    public void Offline_degrades_per_feature_in_all_three_places()
    {
        var main = MainWindow;

        main.Should().Contain("<shell:OfflineStripView",
            "the strip, docked under the toolbar — never a window for a network problem");
        main.Should().Contain("{Binding Updates.IsStale}",
            "the Updates tab keeps its cached rows and badges them");

        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Views", "Shell", "StatusBarView.axaml"))
            .Should().Contain("Classes.stale=\"{Binding IsOffline}\"",
                "zone 2 greys out while it is showing a cached count");
    }

    /// <summary>
    /// 2k · breakpoint 2. Below 900 the two lists become one segmented view, the menu
    /// bar collapses to ☰ and the toolbar keeps only the switch, search, ⋯ and Apply.
    /// <para>
    /// Every one of these is driven from code-behind by name. A renamed or deleted
    /// element makes <c>FindControl</c> return null, the layout silently stays wide,
    /// and nothing throws — so the names are pinned here.
    /// </para>
    /// </summary>
    [Fact]
    public void The_segmented_layout_has_every_element_its_code_behind_drives_by_name()
    {
        MainWindow.Should().Contain("x:Name=\"PaneSplitter\"",
            "hidden when the two lists become one");
        MainWindow.Should().Contain("x:Name=\"InfoPane\"");
        MainWindow.Should().Contain("x:Name=\"InfoDrawerHost\"");

        var toolbar = File.ReadAllText(
            Path.Combine(RepoPaths.AppProject, "Views", "Shell", "ToolbarView.axaml"));

        foreach (var name in new[]
                 {
                     // ColumnsButton is deliberately absent: Columns ▾ went in N1, and
                     // the inactive pane sizes its own columns from its own width.
                     // PaletteDivider/PaletteButton went with the command palette (O10).
                     "ToolbarGrid", "SegmentedSwitch", "InstanceSelector", "InstanceDivider",
                     "SortButton", "UndoRedo", "ApplyButton", "SearchDivider", "FilterChips",
                     "FilterCollapse",
                     "OverflowButton",
                 })
        {
            toolbar.Should().Contain($"x:Name=\"{name}\"",
                $"ToolbarView's code-behind shows and hides {name} by name at the breakpoints");
        }

        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Views", "Shell", "MenuBarView.axaml"))
            .Should().Contain("x:Name=\"VersionAndCounts\"",
                "hidden below 900, where the segmented switch already carries both counts");
    }

    /// <summary>
    /// The two lists are NOT equal halves.
    /// <para>
    /// Measured off screenshot <c>1a</c> at 1440: inactive 300, active 790, mod info
    /// 344 — the load order gets about 2.6x the inactive pane, because it is the one
    /// carrying six columns. A 50/50 split left the NAME column <b>30px</b> wide at the
    /// window's own default size, rendering every mod name as three characters and an
    /// ellipsis, and it looked like a deliberate layout rather than a bug.
    /// </para>
    /// <para>
    /// The ratio is declared twice — here in markup and as
    /// <c>MainWindow.InactivePaneShare</c>, which restores it after 2k's segmented
    /// layout — so both are pinned.
    /// </para>
    /// </summary>
    [Fact]
    public void The_load_order_gets_more_width_than_the_inactive_pane()
    {
        MainWindow.Should().Contain("ColumnDefinitions=\"0.38*,6,1*,6,344\"",
            "1a measures inactive 300 : active 790 : info 344 at 1440");

        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "MainWindow.axaml.cs"))
            .Should().Contain("InactivePaneShare = 0.38",
                "leaving the segmented layout restores this ratio, not 1:1");
    }

    /// <summary>Splits markup into individual element tags, attributes included.</summary>
    private static IEnumerable<string> Elements(string markup)
    {
        var index = 0;
        while ((index = markup.IndexOf('<', index)) >= 0)
        {
            var end = markup.IndexOf('>', index);
            if (end < 0) yield break;

            yield return markup[index..end];
            index = end + 1;
        }
    }

    /// <summary>
    /// "Classes.x" is an element attribute, not a Style setter. Written as a setter
    /// it silently disables the whole style block — which is exactly how the
    /// row-visibility binding was lost the second time.
    /// </summary>
    [Fact]
    public void No_style_setter_tries_to_set_a_bound_class()
    {
        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            File.ReadAllText(file).Should().NotContain("<Setter Property=\"Classes.",
                $"{Path.GetFileName(file)} — bind Classes.x on an element instead");
        }
    }
}
