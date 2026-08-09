using System.Text.RegularExpressions;
using System.Linq;
using System.IO;
using FluentAssertions;
using RimManager.App.Shortcuts;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The shortcut table is the single source of truth for the menu bar, the window
/// key bindings, the ⌘K palette and the ⌘/ sheet (guide §6). These tests are what
/// stop those four from drifting apart.
/// </summary>
public sealed class ShortcutTableTests
{
    [Fact]
    public void Ids_are_unique()
    {
        var ids = ShortcutTable.All.Select(s => s.Id).ToArray();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_entry_has_a_label()
    {
        foreach (var def in ShortcutTable.All)
            def.Label.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Two commands sharing a gesture means one of them silently never fires.
    /// Menu-only entries (no key) are exempt.
    /// </summary>
    [Fact]
    public void No_two_shortcuts_share_a_gesture()
    {
        var duplicates = ShortcutTable.All
            .Where(s => !s.IsUnbound)
            .GroupBy(s => (s.Modifiers, s.Key))
            .Where(g => g.Count() > 1)
            .Select(g => $"{ShortcutFormatter.Format(g.First(), isMac: false)} -> {string.Join(", ", g.Select(x => x.Id))}")
            .ToArray();

        duplicates.Should().BeEmpty();
    }

    /// <summary>Get throws on an unknown id so a typo in a menu binding fails loudly
    /// rather than rendering a blank gesture.</summary>
    [Fact]
    public void Get_throws_on_an_unknown_id()
    {
        var act = () => ShortcutTable.Get("nope.not.a.command");
        act.Should().Throw<KeyNotFoundException>();
    }

    /// <summary>All four sheet groups are populated — 3d prints four blocks.</summary>
    [Theory]
    [InlineData(ShortcutGroup.LoadOrder)]
    [InlineData(ShortcutGroup.Edit)]
    [InlineData(ShortcutGroup.Actions)]
    [InlineData(ShortcutGroup.Navigate)]
    public void Every_sheet_group_has_entries(ShortcutGroup group) =>
        ShortcutTable.ForSheet(group).Should().NotBeEmpty();

    /// <summary>
    /// Labels are identical on all three platforms; only the modifier glyph
    /// differs (guide §6). ⌘ on macOS, Ctrl everywhere else — from one table.
    /// </summary>
    [Fact]
    public void Primary_modifier_renders_as_Cmd_on_mac_and_Ctrl_elsewhere()
    {
        var sort = ShortcutTable.Get(ShortcutTable.SortLoadOrder);

        ShortcutFormatter.Format(sort, isMac: true).Should().Be("⇧⌘S");
        ShortcutFormatter.Format(sort, isMac: false).Should().Be("Ctrl+Shift+S");
    }

    /// <summary>
    /// Only the MODIFIER glyph changes between platforms — 3d's footer says so
    /// explicitly ("⌘ shown on macOS · Ctrl on Windows and Linux"), so the key
    /// itself renders as the same arrow on all three.
    /// </summary>
    [Theory]
    [InlineData(ShortcutTable.ActivateSelected, "Alt+→", "⌥→")]
    [InlineData(ShortcutTable.MoveUp, "Alt+↑", "⌥↑")]
    [InlineData(ShortcutTable.RefreshFolders, "F5", "F5")]
    [InlineData(ShortcutTable.Settings, "Ctrl+,", "⌘,")]
    [InlineData(ShortcutTable.ShortcutSheet, "Ctrl+/", "⌘/")]
    [InlineData(ShortcutTable.InactivePane, "Ctrl+1", "⌘1")]
    [InlineData(ShortcutTable.CollapseAllGroups, "Ctrl+Alt+0", "⌥⌘0")]
    public void Formats_the_key_the_way_a_human_reads_it(string id, string windows, string mac)
    {
        var def = ShortcutTable.Get(id);
        ShortcutFormatter.Format(def, isMac: false).Should().Be(windows);
        ShortcutFormatter.Format(def, isMac: true).Should().Be(mac);
    }

    /// <summary>The gesture string has to be what Avalonia's KeyGesture.Parse takes,
    /// with Meta (not Ctrl) standing in for ⌘ on macOS.</summary>
    [Fact]
    public void Gesture_strings_use_Meta_on_mac()
    {
        var apply = ShortcutTable.Get(ShortcutTable.ApplyToGame);

        ShortcutFormatter.ToGestureString(apply, isMac: false).Should().Be("Ctrl+S");
        ShortcutFormatter.ToGestureString(apply, isMac: true).Should().Be("Meta+S");
    }

    /// <summary>Every bound entry must produce a real Avalonia gesture — a Key name
    /// that does not parse would silently drop the binding.</summary>
    [Fact]
    public void Every_bound_shortcut_resolves_to_an_Avalonia_gesture()
    {
        foreach (var def in ShortcutTable.All.Where(s => !s.IsUnbound))
            ShortcutGesture.For(def).Should().NotBeNull($"'{def.Id}' declares key '{def.Key}'");
    }

    /// <summary>Menu-only entries render no gesture rather than a stray "+".</summary>
    [Fact]
    public void Unbound_entries_render_nothing()
    {
        var manage = ShortcutTable.Get(ShortcutTable.ManageModlists);

        manage.IsUnbound.Should().BeTrue();
        ShortcutFormatter.Format(manage, isMac: false).Should().BeEmpty();
        ShortcutGesture.For(manage).Should().BeNull();
    }

    /// <summary>
    /// The shortcuts screenshot 3d is the ground truth for these; README.md's prose
    /// menu table disagrees (it has Sort on ⌘R and Apply on ⌘↵). Pinning them here
    /// records which source won.
    /// </summary>
    [Fact]
    public void Matches_the_shortcut_sheet_screenshot()
    {
        string Win(string id) => ShortcutFormatter.Format(ShortcutTable.Get(id), isMac: false);

        Win(ShortcutTable.SortLoadOrder).Should().Be("Ctrl+Shift+S");
        Win(ShortcutTable.ApplyToGame).Should().Be("Ctrl+S");
        Win(ShortcutTable.ApplyAndLaunch).Should().Be("Ctrl+Enter");
        Win(ShortcutTable.ValidateNow).Should().Be("Ctrl+R");
        Win(ShortcutTable.ScanConflicts).Should().Be("Ctrl+Shift+C");
        Win(ShortcutTable.InsertSeparator).Should().Be("Ctrl+Shift+N");
        Win(ShortcutTable.Snapshots).Should().Be("Ctrl+Shift+H");
        Win(ShortcutTable.EditNote).Should().Be("Ctrl+Shift+E");
    }

    /// <summary>
    /// Redo is the one entry that deviates from screenshot <c>3d</c>, and the one entry
    /// where the platforms want different <em>chords</em> rather than the same chord with a
    /// different modifier.
    /// <para>
    /// <c>Ctrl+Y</c> is the Windows convention and what a Windows user reaches for; ⇧⌘Z is
    /// universal on macOS, where ⌘Y is not Redo at all. Setting <c>Primary+Y</c> everywhere
    /// — the literal reading of the plan item — would have fixed Windows by breaking macOS,
    /// silently, on a platform nobody here tests on.
    /// </para>
    /// <para>
    /// Asserted for BOTH platforms from one machine, which is the point of
    /// <c>ShortcutDef.On</c>: the running OS decides nothing here.
    /// </para>
    /// </summary>
    [Fact]
    public void Redo_takes_each_platforms_own_convention()
    {
        var redo = ShortcutTable.Get(ShortcutTable.Redo);

        ShortcutFormatter.Format(redo, isMac: false).Should().Be("Ctrl+Y");
        ShortcutFormatter.Format(redo, isMac: true).Should().Be("⇧⌘Z");

        // What is BOUND has to match what is printed, on each platform.
        ShortcutFormatter.ToGestureString(redo, isMac: false).Should().Be("Ctrl+Y");
        ShortcutFormatter.ToGestureString(redo, isMac: true).Should().Be("Meta+Shift+Z");
    }

    /// <summary>
    /// Undo is untouched and must stay so — the platforms agree about it, and a mac
    /// override on an entry that does not need one is a second answer waiting to drift.
    /// </summary>
    [Fact]
    public void Redo_is_the_only_entry_with_a_platform_specific_chord()
    {
        ShortcutTable.All
            .Where(d => d.MacKey is not null || d.MacModifiers is not null)
            .Select(d => d.Id)
            .Should().Equal([ShortcutTable.Redo]);
    }

    // --- where a shortcut may be written (N3 · UI-12) -------------------------

    private static string Hub => RepoPaths.HubSource();

    /// <summary>
    /// The hub exposes no gesture TEXT for chrome to render.
    /// <para>
    /// Four such properties fed four button labels — Dock ⌘J, Rescan ⌘⇧C, Revalidate
    /// ⌘R, Mod Info ⌘3 — and a gesture stapled to a button you are already looking at
    /// teaches nothing, because you are about to click it. The surfaces that keep a key
    /// each say it at the moment it is worth knowing: menus (where every desktop app
    /// teaches them), the palette's key column, and the status bar's "Undo ⌘Z". None of
    /// those goes through a property here, so this stays a clean line to hold.
    /// </para>
    /// <para>
    /// Settings ▸ Tags keeps its slot column and is a different class entirely: that
    /// column IS the assignment control, not an advertisement of one.
    /// </para>
    /// </summary>
    [Fact]
    public void The_hub_exposes_no_gesture_text_for_chrome_to_render()
    {
        var offenders = Regex.Matches(Hub, @"public\s+(?:static\s+)?string\s+(\w*(?:Shortcut|GestureText))\s*=>")
            .Select(m => m.Groups[1].Value)
            .ToList();

        offenders.Should().BeEmpty(
            "a gesture rendered beside a button is noise; menus, the palette and the "
            + "shortcut sheet are where a key is written");
    }

    /// <summary>
    /// Every KeyGesture the hub exposes is actually bound by something.
    /// <para>
    /// Eleven were not — Undo, Redo, Sort, Validate, ScanConflicts, CheckUpdates,
    /// Refresh, Dock, Palette, Separator and ModInfoPane — because the menu bar builds
    /// its gestures from MenuItemViewModel and the window's bindings come from
    /// ShortcutBindings, so these were a third route nothing ever took. Dead code that
    /// looks exactly like working code, which is this project's most expensive shape.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_gesture_the_hub_exposes_is_bound_by_something()
    {
        var declared = Regex.Matches(Hub, @"public\s+static\s+[\w.?]*KeyGesture\?\s+(\w+)\s*=>")
            .Select(m => m.Groups[1].Value)
            .ToList();

        var markup = string.Concat(Directory
            .EnumerateFiles(RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));

        declared.Should().OnlyContain(name => markup.Contains(name),
            "a gesture property nothing binds is a third route to a shortcut that "
            + "nobody takes, and it reads as though the menu depends on it");
    }
}
