using System.Windows.Input;
using FluentAssertions;
using RimManager.App.Shortcuts;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The window's key bindings, generated from <see cref="ShortcutTable"/>.
/// <para>
/// R0's rule was one table for the menu labels, the key bindings, the ⌘K palette and
/// the ⌘/ sheet. Three of the four were built from it; the bindings were written out by
/// hand — eleven, against a table that grew to forty-six. Avalonia's
/// <c>MenuItem.InputGesture</c> only DRAWS a gesture, so seven shortcuts with a working
/// command behind them printed themselves in the menus and did nothing when pressed.
/// </para>
/// </summary>
public sealed class ShortcutBindingsTests
{
    private sealed class Noop : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    private static readonly ICommand Command = new Noop();

    /// <summary>
    /// The seven that were advertised, implemented and unbound. Named individually,
    /// because "the count went up" would not have caught which ones were missing.
    /// </summary>
    [Theory]
    [InlineData(ShortcutTable.Settings)]
    [InlineData(ShortcutTable.Quit)]
    [InlineData(ShortcutTable.NewModlist)]
    [InlineData(ShortcutTable.ExportModList)]
    [InlineData(ShortcutTable.ImportCollection)]
    [InlineData(ShortcutTable.ApplyAndLaunch)]
    [InlineData(ShortcutTable.ShortcutSheet)]
    [InlineData(ShortcutTable.ModInfoPane)]
    public void A_shortcut_with_a_gesture_and_a_command_is_bound(string id)
    {
        var bound = ShortcutBindings.For(_ => Command);

        bound.Should().Contain(b => b.Gesture.Equals(ShortcutGesture.For(id)),
            $"'{id}' prints a gesture in the menus, so pressing it must do what it says");
    }

    /// <summary>
    /// An id with no command yet renders visible-but-disabled in the menus (R2a's
    /// rule). It must not get a binding either, or a greyed menu item and a live
    /// keystroke would disagree about whether the feature exists.
    /// </summary>
    [Fact]
    public void An_id_with_no_command_gets_no_binding()
    {
        ShortcutBindings.For(_ => null).Should().BeEmpty();
    }

    /// <summary>
    /// Entries that carry no gesture — Sync community rules, About, Getting started —
    /// are menu-only. Binding them would mean inventing a keystroke the sheet never
    /// showed anyone.
    /// </summary>
    [Fact]
    public void Entries_with_no_gesture_are_menu_only()
    {
        var bound = ShortcutBindings.For(_ => Command);
        var withGestures = ShortcutTable.All.Count(e => ShortcutGesture.For(e.Id) is not null);

        bound.Should().HaveCount(withGestures);
        bound.Should().OnlyContain(b => b.Gesture != null);
    }

    /// <summary>
    /// Two bindings on one gesture means the first one silently wins. ShortcutTable
    /// already forbids duplicate gestures; this asserts the generated list inherits it,
    /// since that is the property the window actually depends on.
    /// </summary>
    [Fact]
    public void No_gesture_is_bound_twice()
    {
        var bound = ShortcutBindings.For(_ => Command);

        bound.Select(b => b.Gesture.ToString())
            .Should().OnlyHaveUniqueItems();
    }
}
