using FluentAssertions;
using RimManager.App.Shortcuts;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The menu bar is generated from <see cref="ShortcutTable"/>, so these tests are
/// what stop a menu row from advertising a shortcut the key bindings never honour.
/// Grouping follows screenshot 2h.
/// </summary>
public sealed class MenuModelTests
{
    [Fact]
    public void Has_the_five_menus_in_order() =>
        MenuModel.Menus.Select(m => m.Title).Should().Equal("File", "Edit", "View", "Tools", "Help");

    /// <summary>
    /// Menu items that are supposed to WORK must resolve to a command.
    /// <para>
    /// R2a made the menu data-driven and decided that an id with no command renders
    /// visible-but-disabled, so a later phase's surfaces still teach their shortcuts.
    /// The cost of that rule is that removing a Click handler silently disables a menu
    /// item instead of breaking the build — which is exactly what happened to File ▸
    /// New Instance, Settings and Quit, dead from R2a until someone clicked one.
    /// </para>
    /// <para>
    /// This list is the set that must never be disabled. Anything absent from it is
    /// allowed to be greyed, and adding an entry here is the deliberate act of saying
    /// "this one is finished".
    /// </para>
    /// </summary>
    [Fact]
    public void Every_menu_item_that_should_work_resolves_to_a_command()
    {
        string[] mustWork =
        [
            ShortcutTable.NewModlist,
            ShortcutTable.Settings,
            ShortcutTable.Quit,
            ShortcutTable.Undo,
            ShortcutTable.Redo,
            ShortcutTable.SortLoadOrder,
            ShortcutTable.ApplyToGame,

            // Dead from R2a until R6g: the shortcut table listed them and the menu drew
            // them, but nothing was ever wired, so both rendered permanently greyed.
            ShortcutTable.ApplyAndLaunch,
            ShortcutTable.LaunchOnly,

            // R7: the help surfaces exist now, so they must not be greyed.
            ShortcutTable.ShortcutSheet,
            ShortcutTable.About,
            ShortcutTable.CheckUpdates,
            ShortcutTable.ScanConflicts,
            ShortcutTable.SyncRules,
            ShortcutTable.RefreshFolders,
            ShortcutTable.ImportModList,
            ShortcutTable.ExportModList,
            ShortcutTable.InsertSeparator,

            // N4e: Rename separator (F2). It was in the table and in the Load-order menu
            // and routed to nothing, so the key did nothing and the row rendered greyed —
            // for a feature that has worked all along from the row's own ⋮ menu. The N4a
            // shape, in the one surface the audit's markup guard cannot see, because this
            // menu is built from data rather than from markup.
            ShortcutTable.RenameSeparator,

            ShortcutTable.BottomDock,

            // R7g: the import wizard (2i-3). Dead since R2a until now.
            ShortcutTable.ImportCollection,

            // R8: Help ▸ Re-run first-time setup, dead for the same reason.
            ShortcutTable.RerunFirstRun,

            // R9d: View ▸ Mod info pane. It is the ONLY way back to 2k's drawer once
            // it is closed, which is how it was finally noticed.
            ShortcutTable.ModInfoPane,

            // The UI-audit wave: every one of these rendered greyed (and its gesture
            // dead) while its operation existed somewhere else in the app.
            ShortcutTable.RuleEditor,
            ShortcutTable.Snapshots,
            ShortcutTable.CollapseAllGroups,
            ShortcutTable.ResetLayout,
            ShortcutTable.OpenLogFolder,
            ShortcutTable.CopyDiagnostics,
            ShortcutTable.ActivateSelected,
            ShortcutTable.DeactivateSelected,
            ShortcutTable.MoveUp,
            ShortcutTable.MoveDown,
            ShortcutTable.SelectAll,
            ShortcutTable.CopyPackageId,
            ShortcutTable.ToggleFavorite,
            ShortcutTable.EditNote,
            ShortcutTable.InactivePane,
            ShortcutTable.FocusSearch,
            ShortcutTable.ReportIssue,
            ShortcutTable.DensityCompact,
            ShortcutTable.DensityComfortable,
            ShortcutTable.FocusDockWarnings,
            ShortcutTable.FocusDockUpdates,
            ShortcutTable.FocusDockHistory,
            ShortcutTable.FocusDockActivity,
            ShortcutTable.SortAlphabetical,

            // Both "Sort with…" rows must force their mode. The topological row used to
            // route to SortLoadOrder, which honours the stored preference — so the row
            // resolved to a command that could do the opposite of its label.
            ShortcutTable.SortTopological,
        ];

        var vm = TestViewModel();
        foreach (var id in mustWork)
        {
            vm.HasCommandFor(id).Should().BeTrue(
                $"'{id}' is presented as usable, so it must route to a command — "
                + "an unrouted id renders visible-but-disabled and clicking it does nothing");
        }
    }

    /// <summary>
    /// A view model with no instance loaded. Enough for command routing, which is a
    /// pure switch over ids — and the point of keeping the view model constructible
    /// without a window.
    /// </summary>
    private static ViewModels.MainWindowViewModel TestViewModel() =>
        (ViewModels.MainWindowViewModel)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(ViewModels.MainWindowViewModel));

    /// <summary>
    /// The join that matters: every id a menu references must exist in the table.
    /// A typo here would otherwise render a blank gesture and a dead command.
    /// </summary>
    [Fact]
    public void Every_referenced_shortcut_id_resolves()
    {
        foreach (var id in MenuModel.ReferencedShortcutIds())
        {
            var act = () => ShortcutTable.Get(id);
            act.Should().NotThrow($"menu references '{id}'");
        }
    }

    [Fact]
    public void Labels_come_from_the_shortcut_table_unless_deliberately_overridden()
    {
        var tools = MenuModel.Menus.Single(m => m.Title == "Tools");
        var sort = tools.Rows.First(r => r.ShortcutId == ShortcutTable.SortLoadOrder);

        sort.DisplayLabel.Should().Be(ShortcutTable.Get(ShortcutTable.SortLoadOrder).Label);
    }

    [Fact]
    public void An_override_wins_over_the_table_label()
    {
        var file = MenuModel.Menus.Single(m => m.Title == "File");
        var settings = file.Rows.First(r => r.ShortcutId == ShortcutTable.Settings);

        settings.DisplayLabel.Should().Be("Settings…");
    }

    [Fact]
    public void Separators_are_rows_with_nothing_in_them()
    {
        MenuRow.Separator.IsSeparator.Should().BeTrue();
        MenuRow.Item(ShortcutTable.Quit).IsSeparator.Should().BeFalse();
        MenuRow.Submenu("Density").IsSeparator.Should().BeFalse();
    }

    [Fact]
    public void Every_menu_has_rows_and_none_starts_or_ends_with_a_separator()
    {
        foreach (var menu in MenuModel.Menus)
        {
            menu.Rows.Should().NotBeEmpty();
            menu.Rows[0].IsSeparator.Should().BeFalse($"{menu.Title} must not open with a rule");
            menu.Rows[^1].IsSeparator.Should().BeFalse($"{menu.Title} must not end with a rule");
        }
    }

    [Fact]
    public void No_two_separators_are_adjacent()
    {
        foreach (var menu in MenuModel.Menus)
        {
            for (var i = 1; i < menu.Rows.Length; i++)
            {
                (menu.Rows[i].IsSeparator && menu.Rows[i - 1].IsSeparator)
                    .Should().BeFalse($"{menu.Title} has a doubled rule at {i}");
            }
        }
    }

    /// <summary>
    /// The submenus that survive carry CHILDREN — a submenu with none renders as a
    /// dead leaf, which is how Density/Theme/Columns/Focus dock tab/Sort with…/Add
    /// tag spent nine phases opening onto nothing (UI audit). Theme, Columns and
    /// Add-tag are deliberately GONE: the gallery, the pane's Columns ▾ and the
    /// context menu are their real surfaces.
    /// </summary>
    [Fact]
    public void Every_surviving_submenu_has_children_and_the_dead_ones_are_gone()
    {
        var submenus = MenuModel.Menus
            .SelectMany(m => m.Rows)
            .Where(r => r.ShortcutId is null && r.Label is not null && !r.IsCheckable)
            .ToList();

        submenus.Select(r => r.Label).Should().Contain(["Focus dock tab", "Density", "Sort with…"]);
        submenus.Should().OnlyContain(r => !r.ChildrenOrEmpty.IsEmpty,
            "a submenu with no children renders as a leaf that opens onto nothing");

        var labels = submenus.Select(r => r.Label).ToList();
        labels.Should().NotContain(["Theme", "Columns", "Add tag to selection"]);

        // "Switch instance" is deliberately NOT here either (the modlist migration):
        // the switcher lives on the toolbar and the narrow layout's overflow.
        labels.Should().NotContain("Switch instance");
        labels.Should().NotContain("Switch modlist");
    }

    /// <summary>
    /// NO View row is checkable any more (UI audit): the pane rows are FOCUS actions
    /// wearing Item shape, and the three preference checkboxes (stripes/zebra/group)
    /// died — each bound its ✓ to a value nothing read while the real preference
    /// lived in Settings. A checkbox that lies is worse than no checkbox.
    /// </summary>
    [Fact]
    public void The_view_menu_carries_no_dead_checkboxes()
    {
        var view = MenuModel.Menus.Single(m => m.Title == "View");

        view.Rows.Should().NotContain(r => r.IsCheckable);
        view.Rows.Should().NotContain(r => r.Label == "Tag colour stripes");
        view.Rows.First(r => r.ShortcutId == ShortcutTable.InactivePane)
            .IsCheckable.Should().BeFalse("focusing a pane is an action, not a toggle");
    }

    /// <summary>
    /// The whole point of the exercise: what the menu prints for a command equals
    /// what the shortcut sheet prints for it, because both read one table.
    /// </summary>
    [Fact]
    public void Menu_gesture_text_matches_the_shortcut_sheet()
    {
        var tools = MenuModel.Menus.Single(m => m.Title == "Tools");
        var apply = tools.Rows.First(r => r.ShortcutId == ShortcutTable.ApplyToGame);

        ShortcutFormatter.Format(ShortcutTable.Get(apply.ShortcutId!), isMac: false)
            .Should().Be("Ctrl+S");
    }
}
