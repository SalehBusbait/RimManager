using System.Collections.Immutable;

namespace RimManager.App.Shortcuts;

/// <summary>
/// One row of a menu: a command, a submenu, or a separator rule.
/// </summary>
/// <param name="ShortcutId">
/// The <see cref="ShortcutTable"/> id this row invokes. Null for a separator or a
/// pure submenu header. The label and the gesture are both read from the table, so a
/// menu can never show a shortcut the key bindings do not honour.
/// </param>
/// <param name="Label">
/// Overrides the table's label. Used where the menu wants different wording from the
/// shortcut sheet — the sheet says "Move up / down", the Edit menu lists them apart.
/// </param>
/// <param name="Children">Submenu rows, for items rendered with a ▸.</param>
/// <param name="IsCheckable">Renders with a ✓ slot (View ▸ Tag colour stripes).</param>
public sealed record MenuRow(
    string? ShortcutId = null,
    string? Label = null,
    ImmutableArray<MenuRow> Children = default,
    bool IsCheckable = false)
{
    /// <summary>A 1px rule with 4px margins (`2h`).</summary>
    public static readonly MenuRow Separator = new();

    public bool IsSeparator => ShortcutId is null && Label is null && Children.IsDefaultOrEmpty;

    public ImmutableArray<MenuRow> ChildrenOrEmpty => Children.IsDefault ? [] : Children;

    /// <summary>The text shown, preferring the override then the shortcut table.</summary>
    public string DisplayLabel =>
        Label ?? (ShortcutId is not null ? ShortcutTable.Get(ShortcutId).Label : string.Empty);

    public static MenuRow Item(string shortcutId, string? label = null) => new(shortcutId, label);

    public static MenuRow Check(string? shortcutId, string? label = null) =>
        new(shortcutId, label, IsCheckable: true);

    public static MenuRow Submenu(string label, params MenuRow[] children) =>
        new(null, label, [.. children]);
}

/// <summary>A top-level menu: File, Edit, View, Tools, Help.</summary>
public sealed record MenuDefinition(string Title, ImmutableArray<MenuRow> Rows);

/// <summary>
/// The menu bar, as data.
/// <para>
/// Built from <see cref="ShortcutTable"/> rather than hand-authored XAML so the menu
/// labels, the key bindings and the ⌘/ sheet all come from one place
/// — guide §6: "Bind InputGesture from a single shortcut table so the menu label and
/// the actual KeyBinding can never disagree."
/// </para>
/// <para>
/// Grouping and separator placement follow screenshot <c>2h</c>. Item labels come
/// from the table; only where the menu deliberately words something differently does
/// a row override it.
/// </para>
/// </summary>
public static class MenuModel
{
    public static readonly ImmutableArray<MenuDefinition> Menus =
    [
        new("File",
        [
            MenuRow.Item(ShortcutTable.NewModlist),

            // No "Switch modlist" submenu. It was "Switch instance" and it was declared
            // with NO CHILDREN, so it rendered as a menu that opened onto nothing —
            // a dead control in the most-used menu. The switcher is the toolbar's
            // leftmost button and the narrow layout's overflow menu; a third route that
            // has to be kept populated is a third thing to get wrong.
            MenuRow.Item(ShortcutTable.ManageModlists),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.ImportCollection),
            MenuRow.Item(ShortcutTable.ImportModList),
            MenuRow.Item(ShortcutTable.ExportModList),
            MenuRow.Item(ShortcutTable.ExportWorkshopItem),
            MenuRow.Item(ShortcutTable.ExportCollection),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.Settings, "Settings…"),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.Quit),
        ]),

        new("Edit",
        [
            // Undo names the action it will undo ("Undo move 4 mods") — the label is
            // rewritten at runtime, which is why it carries an override slot.
            MenuRow.Item(ShortcutTable.Undo),
            MenuRow.Item(ShortcutTable.Redo),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.ActivateSelected),
            MenuRow.Item(ShortcutTable.DeactivateSelected),
            MenuRow.Item(ShortcutTable.MoveUp),
            MenuRow.Item(ShortcutTable.MoveDown),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.InsertSeparator),
            MenuRow.Item(ShortcutTable.RenameSeparator),
            MenuRow.Separator,
            // "Add tag to selection" is GONE (UI audit): the menu is static data built
            // once, and a tag list is live user data — a submenu that cannot follow it
            // would go stale on the first new tag. Tags are assigned from Mod Info and
            // the row context menu, which rebuild per open.
            MenuRow.Item(ShortcutTable.EditNote, "Edit note…"),
            MenuRow.Item(ShortcutTable.ToggleFavorite),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.SelectAll),
            MenuRow.Item(ShortcutTable.CopyPackageId),
        ]),

        new("View",
        [
            // Items, not Checks (UI audit): both rows FOCUS a pane — the check slot
            // implied a visibility toggle whose state nothing tracked, so the ✓ was
            // permanently blank over an action that is not a toggle.
            MenuRow.Item(ShortcutTable.InactivePane),
            MenuRow.Item(ShortcutTable.ModInfoPane),
            MenuRow.Item(ShortcutTable.BottomDock, "Bottom dock"),
            MenuRow.Submenu("Focus dock tab",
                MenuRow.Item(ShortcutTable.FocusDockWarnings),
                MenuRow.Item(ShortcutTable.FocusDockUpdates),
                MenuRow.Item(ShortcutTable.FocusDockHistory),
                MenuRow.Item(ShortcutTable.FocusDockActivity)),
            MenuRow.Separator,
            MenuRow.Submenu("Density",
                MenuRow.Item(ShortcutTable.DensityCompact),
                MenuRow.Item(ShortcutTable.DensityComfortable)),
            // "Theme" and "Columns" submenus are GONE (UI audit): both were declared
            // with no children and rendered as dead leaves for nine phases. The theme
            // gallery (Settings ▸ Appearance) and the inactive pane's own Columns ▾
            // are the real surfaces; an 11-way theme submenu would be a second copy of
            // a choice that already has a better home. The three dead check rows (Tag
            // colour stripes / Zebra / Group by separator) died with them: each bound
            // its ✓ to a value nothing read while the real preference lived in
            // Settings — a checkbox that lies is worse than no checkbox.
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.CollapseAllGroups),
            MenuRow.Item(ShortcutTable.ResetLayout),
        ]),

        new("Tools",
        [
            MenuRow.Item(ShortcutTable.SortLoadOrder),
            // Both rows FORCE their mode. The topological row ran SortLoadOrder, which
            // honours the stored preference, so with alphabetical chosen it sorted
            // alphabetically under a label saying otherwise.
            MenuRow.Submenu("Sort with…",
                MenuRow.Item(ShortcutTable.SortTopological),
                MenuRow.Item(ShortcutTable.SortAlphabetical)),
            MenuRow.Item(ShortcutTable.ValidateNow),
            MenuRow.Item(ShortcutTable.ScanConflicts),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.ApplyToGame),
            MenuRow.Item(ShortcutTable.ApplyAndLaunch),
            MenuRow.Item(ShortcutTable.LaunchOnly),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.CheckUpdates),
            MenuRow.Item(ShortcutTable.RefreshFolders),
            MenuRow.Item(ShortcutTable.Snapshots, "Snapshots…"),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.SyncRules),
            MenuRow.Item(ShortcutTable.RuleEditor),
        ]),

        // "Getting started" and "Check for updates…" are GONE (UI audit): the first
        // had nothing to open until N12's documentation exists, and the second
        // promised the self-updater R7 decided against. Rows return when their
        // features do.
        new("Help",
        [
            MenuRow.Item(ShortcutTable.ShortcutSheet),
            MenuRow.Item(ShortcutTable.RerunFirstRun),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.OpenLogFolder),
            MenuRow.Item(ShortcutTable.CopyDiagnostics),
            MenuRow.Item(ShortcutTable.ReportIssue, "Report an issue ↗"),
            MenuRow.Separator,
            MenuRow.Item(ShortcutTable.About),
        ]),
    ];

    /// <summary>Every shortcut id referenced by a menu row, submenus included.</summary>
    public static IEnumerable<string> ReferencedShortcutIds() =>
        Menus.SelectMany(m => Flatten(m.Rows))
             .Select(r => r.ShortcutId)
             .Where(id => id is not null)!;

    private static IEnumerable<MenuRow> Flatten(ImmutableArray<MenuRow> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            foreach (var child in Flatten(row.ChildrenOrEmpty)) yield return child;
        }
    }
}
