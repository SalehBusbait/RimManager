using System.Collections.Immutable;

namespace RimManager.App.Shortcuts;

/// <summary>
/// Modifier keys, platform-neutral. <see cref="KeyMod.Primary"/> is ⌘ on macOS and Ctrl
/// everywhere else — the whole point of not storing a platform-specific gesture.
/// </summary>
[Flags]
public enum KeyMod
{
    None = 0,
    Primary = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>Which block of the ⌘/ sheet (3d) an entry is printed under.</summary>
public enum ShortcutGroup
{
    LoadOrder,
    Edit,
    Actions,
    Navigate,
}

/// <summary>
/// One row of the shortcut table.
/// </summary>
/// <param name="Id">Stable identifier; what a menu item, key binding and palette entry all key off.</param>
/// <param name="Label">Human label, identical on all three platforms (guide §6).</param>
/// <param name="Group">The ⌘/ sheet block. Null keeps it out of the sheet.</param>
/// <param name="Modifiers">Platform-neutral modifiers.</param>
/// <param name="Key">Avalonia <c>Key</c> enum name: <c>S</c>, <c>F5</c>, <c>Up</c>, <c>Return</c>, <c>D1</c>…</param>
/// <param name="MacModifiers">
/// A macOS-only override, for the rare entry where the platforms want a genuinely
/// <em>different chord</em> rather than the same one with ⌘ for Ctrl. Null means
/// <paramref name="Modifiers"/> applies everywhere, which is true of all but one entry.
/// </param>
/// <param name="MacKey">The macOS-only key. Null means <paramref name="Key"/> applies everywhere.</param>
public sealed record ShortcutDef(
    string Id,
    string Label,
    ShortcutGroup? Group,
    KeyMod Modifiers,
    string Key,
    KeyMod? MacModifiers = null,
    string? MacKey = null)
{
    /// <summary>True when the entry has no key at all — a menu item that only exists in a menu.</summary>
    public bool IsUnbound => string.IsNullOrEmpty(Key);

    /// <summary>
    /// The chord this platform actually uses. <see cref="KeyMod.Primary"/> already absorbs
    /// "⌘ there, Ctrl here"; this absorbs the case where the two platforms disagree about
    /// the chord itself, which is a different thing and had nowhere to live.
    /// </summary>
    public (KeyMod Modifiers, string Key) On(bool isMac) =>
        isMac && MacKey is { } macKey ? (MacModifiers ?? Modifiers, macKey) : (Modifiers, Key);
}

/// <summary>
/// THE single source of truth for every shortcut in the app.
/// <para>
/// The menu bar labels, the window <c>KeyBinding</c>s, the ⌘K palette entries and
/// the ⌘/ sheet are all generated from this one list, so they cannot drift
/// (guide §6; <c>3d</c> is explicitly "generated from the same table that builds
/// the menus — never hand-maintained").
/// </para>
/// <para>
/// Values are taken from screenshots <c>2h</c> (menus) and <c>3d</c> (sheet), which
/// agree with each other. Where <c>README.md</c>'s prose menu table disagrees —
/// it has Sort on ⌘R, Apply on ⌘↵, Export on ⌘S — the screenshots win, per the
/// handoff's own rule that they are the visual ground truth.
/// </para>
/// </summary>
public static class ShortcutTable
{
    // ---- ids ---------------------------------------------------------------
    // Referenced by menu items and commands; keep them stable, they are the join key.
    public const string NewModlist = "modlist.new";
    public const string ManageModlists = "modlist.manage";
    public const string ImportCollection = "import.collection";
    public const string ImportModList = "import.modlist";
    public const string ExportModList = "export.modlist";
    public const string ExportWorkshopItem = "export.workshopitem";
    public const string ExportCollection = "export.collection";
    public const string Settings = "app.settings";
    public const string Quit = "app.quit";

    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string ActivateSelected = "order.activate";
    public const string DeactivateSelected = "order.deactivate";
    public const string MoveUp = "order.move-up";
    public const string MoveDown = "order.move-down";
    public const string InsertSeparator = "order.insert-separator";
    public const string RenameSeparator = "order.rename-separator";
    public const string CollapseAllGroups = "order.collapse-all";
    public const string EditNote = "edit.note";
    public const string ToggleFavorite = "edit.favorite";
    public const string SelectAll = "edit.select-all";
    public const string CopyPackageId = "edit.copy-packageid";

    public const string InactivePane = "view.inactive-pane";
    public const string ModInfoPane = "view.info-pane";
    public const string BottomDock = "view.dock";
    public const string ResetLayout = "view.reset-layout";
    public const string DensityCompact = "view.density-compact";
    public const string DensityComfortable = "view.density-comfortable";
    public const string FocusDockWarnings = "nav.dock-warnings";
    public const string FocusDockUpdates = "nav.dock-updates";
    public const string FocusDockHistory = "nav.dock-history";
    public const string FocusDockActivity = "nav.dock-activity";
    public const string SortAlphabetical = "tools.sort-alpha";

    /// <summary>Sort with… ▸ Topological. Distinct from <see cref="SortLoadOrder"/>,
    /// which honours the stored mode: a row in a mode-picking submenu has to force
    /// its mode, exactly as the alphabetical row does.</summary>
    public const string SortTopological = "tools.sort-topo";

    public const string SortLoadOrder = "tools.sort";
    public const string ValidateNow = "tools.validate";
    public const string ScanConflicts = "tools.scan-conflicts";
    public const string ApplyToGame = "tools.apply";
    public const string ApplyAndLaunch = "tools.apply-launch";
    public const string LaunchOnly = "tools.launch";
    public const string CheckUpdates = "tools.check-updates";
    public const string RefreshFolders = "tools.refresh";
    public const string Snapshots = "tools.snapshots";
    public const string SyncRules = "tools.sync-rules";
    public const string RuleEditor = "tools.rule-editor";

    // nav.palette is GONE (O10): the command palette saw no use in daily driving and
    // was a second route to everything the menus already carry.
    public const string FocusSearch = "nav.search";
    public const string ShortcutSheet = "help.shortcuts";
    // help.getting-started and help.app-updates are GONE (UI audit): the first had no
    // content to open until N12's documentation exists, and the second promised the
    // self-updater R7 explicitly decided against — a menu row is a promise, and both
    // rows had spent their whole lives breaking theirs.
    public const string RerunFirstRun = "help.first-run";
    public const string OpenLogFolder = "help.log-folder";
    public const string CopyDiagnostics = "help.diagnostics";
    public const string ReportIssue = "help.report-issue";
    public const string About = "help.about";

    /// <summary>Every shortcut in the app, in sheet order within each group.</summary>
    public static readonly ImmutableArray<ShortcutDef> All =
    [
        // ---- LOAD ORDER (3d, column 1 block 1) -----------------------------
        new(ActivateSelected, "Activate selected", ShortcutGroup.LoadOrder, KeyMod.Alt, "Right"),
        new(DeactivateSelected, "Deactivate selected", ShortcutGroup.LoadOrder, KeyMod.Alt, "Left"),
        new(MoveUp, "Move up", ShortcutGroup.LoadOrder, KeyMod.Alt, "Up"),
        new(MoveDown, "Move down", ShortcutGroup.LoadOrder, KeyMod.Alt, "Down"),
        new(InsertSeparator, "Insert separator", ShortcutGroup.LoadOrder, KeyMod.Primary | KeyMod.Shift, "N"),
        new(RenameSeparator, "Rename separator", ShortcutGroup.LoadOrder, KeyMod.None, "F2"),
        new(CollapseAllGroups, "Collapse all groups", ShortcutGroup.LoadOrder, KeyMod.Primary | KeyMod.Alt, "D0"),

        // ---- EDIT ----------------------------------------------------------
        new(Undo, "Undo", ShortcutGroup.Edit, KeyMod.Primary, "Z"),

        // The one entry where the platforms want different chords rather than the same
        // chord with a different modifier. Ctrl+Y is the Windows convention for Redo and
        // is what a Windows user reaches for; ⇧⌘Z is universal on macOS, where ⌘Y is not
        // Redo at all. Shipping Primary+Y everywhere would fix Windows by breaking macOS.
        //
        // A stated deviation from screenshot 3d, which shows ⌘⇧Z: that screenshot is one
        // platform's answer to a question that has two, and the handoff's "screenshots are
        // the visual ground truth" rule is about appearance, not about key conventions on
        // an OS it was not drawn for.
        new(Redo, "Redo", ShortcutGroup.Edit, KeyMod.Primary, "Y",
            MacModifiers: KeyMod.Primary | KeyMod.Shift, MacKey: "Z"),
        new(SelectAll, "Select all", ShortcutGroup.Edit, KeyMod.Primary, "A"),
        new(CopyPackageId, "Copy packageId", ShortcutGroup.Edit, KeyMod.Primary, "C"),
        new(ToggleFavorite, "Toggle favourite", ShortcutGroup.Edit, KeyMod.Primary, "D"),
        new(EditNote, "Edit note", ShortcutGroup.Edit, KeyMod.Primary | KeyMod.Shift, "E"),

        // ---- ACTIONS -------------------------------------------------------
        new(SortLoadOrder, "Sort load order", ShortcutGroup.Actions, KeyMod.Primary | KeyMod.Shift, "S"),
        new(ApplyToGame, "Apply to game", ShortcutGroup.Actions, KeyMod.Primary, "S"),
        new(ApplyAndLaunch, "Apply and launch RimWorld", ShortcutGroup.Actions, KeyMod.Primary, "Return"),
        new(ValidateNow, "Validate now", ShortcutGroup.Actions, KeyMod.Primary, "R"),
        new(ScanConflicts, "Scan conflicts", ShortcutGroup.Actions, KeyMod.Primary | KeyMod.Shift, "C"),
        new(CheckUpdates, "Check for mod updates", ShortcutGroup.Actions, KeyMod.Primary, "U"),
        new(RefreshFolders, "Refresh mod folders", ShortcutGroup.Actions, KeyMod.None, "F5"),

        // ---- NAVIGATE ------------------------------------------------------
        new(FocusSearch, "Focus search", ShortcutGroup.Navigate, KeyMod.Primary, "F"),
        new(InactivePane, "Inactive pane", ShortcutGroup.Navigate, KeyMod.Primary, "D1"),
        new(ModInfoPane, "Mod info pane", ShortcutGroup.Navigate, KeyMod.Primary, "D3"),
        new(BottomDock, "Toggle dock", ShortcutGroup.Navigate, KeyMod.Primary, "J"),
        new(Snapshots, "Snapshots", ShortcutGroup.Navigate, KeyMod.Primary | KeyMod.Shift, "H"),
        new(Settings, "Settings", ShortcutGroup.Navigate, KeyMod.Primary, "OemComma"),
        new(ShortcutSheet, "Keyboard shortcuts", ShortcutGroup.Navigate, KeyMod.Primary, "OemQuestion"),

        // ---- bound, but not printed on the sheet ---------------------------
        new(NewModlist, "New modlist…", null, KeyMod.Primary, "N"),
        new(ImportCollection, "Import Steam collection…", null, KeyMod.Primary | KeyMod.Shift, "I"),
        new(ExportModList, "Export mod list…", null, KeyMod.Primary, "E"),
        new(Quit, "Quit", null, KeyMod.Primary, "Q"),

        // ---- menu-only, no gesture -----------------------------------------
        new(ManageModlists, "Manage modlists…", null, KeyMod.None, ""),
        new(ImportModList, "Import mod list (.rwlist, .xml)…", null, KeyMod.None, ""),
        new(ExportWorkshopItem, "Export as Workshop item…", null, KeyMod.None, ""),
        new(ExportCollection, "Export as Steam collection…", null, KeyMod.None, ""),
        new(LaunchOnly, "Launch without applying", null, KeyMod.None, ""),
        new(SyncRules, "Sync community rules", null, KeyMod.None, ""),
        new(RuleEditor, "Rule editor…", null, KeyMod.None, ""),
        new(ResetLayout, "Reset layout", null, KeyMod.None, ""),
        new(RerunFirstRun, "Re-run first-time setup…", null, KeyMod.None, ""),
        new(OpenLogFolder, "Open log folder", null, KeyMod.None, ""),
        new(CopyDiagnostics, "Copy diagnostics bundle", null, KeyMod.None, ""),
        new(ReportIssue, "Report an issue", null, KeyMod.None, ""),
        new(About, "About RimManager", null, KeyMod.None, ""),
        new(DensityCompact, "Compact 20px rows", null, KeyMod.None, ""),
        new(DensityComfortable, "Comfortable 26px rows", null, KeyMod.None, ""),
        new(FocusDockWarnings, "Warnings", null, KeyMod.None, ""),
        new(FocusDockUpdates, "Updates", null, KeyMod.None, ""),
        new(FocusDockHistory, "History", null, KeyMod.None, ""),
        new(FocusDockActivity, "Activity", null, KeyMod.None, ""),
        new(SortAlphabetical, "Alphabetical within separators", null, KeyMod.None, ""),
        new(SortTopological, "Topological (rules)", null, KeyMod.None, ""),
    ];

    private static readonly ImmutableDictionary<string, ShortcutDef> ById =
        All.ToImmutableDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>Looks a shortcut up by id. Throws if the id is unknown — a typo in a
    /// menu binding should fail loudly at startup, not render a blank gesture.</summary>
    public static ShortcutDef Get(string id) =>
        ById.TryGetValue(id, out var def)
            ? def
            : throw new KeyNotFoundException($"No shortcut registered for id '{id}'.");

    public static bool TryGet(string id, out ShortcutDef? def)
    {
        var found = ById.TryGetValue(id, out var d);
        def = d;
        return found;
    }

    /// <summary>The sheet's entries for one group, in table order (3d).</summary>
    public static ImmutableArray<ShortcutDef> ForSheet(ShortcutGroup group) =>
        [.. All.Where(s => s.Group == group && !s.IsUnbound)];
}
