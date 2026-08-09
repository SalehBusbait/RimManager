using System.Windows.Input;
using Avalonia.Input;

namespace RimManager.App.Shortcuts;

/// <summary>
/// The window's key bindings, GENERATED from <see cref="ShortcutTable"/> — the same
/// table the menu labels, the ⌘K palette and the ⌘/ sheet are built from.
/// <para>
/// R0 set the rule that one table feeds all four surfaces and then wired only three of
/// them: the bindings were hand-written in <c>MainWindow.axaml</c>, eleven of them, and
/// the table has grown to forty-six. Avalonia's <c>MenuItem.InputGesture</c> is
/// display-only — it draws the gesture and invokes nothing — so seven shortcuts were
/// printed in the menus, backed by a working command, and did nothing when pressed:
/// Settings, Quit, New instance, Export mod list, Import Steam collection, Apply and
/// launch, and the keyboard-shortcut sheet itself. Ctrl+3 was an eighth, found only
/// because 2k's drawer made it the one way back.
/// </para>
/// <para>
/// Generated, that class of bug cannot recur: a shortcut works the moment its command
/// exists, and an id with no command is skipped rather than bound to nothing.
/// </para>
/// </summary>
public static class ShortcutBindings
{
    /// <summary>
    /// Every table entry that has both a gesture and a command, in table order.
    /// </summary>
    /// <param name="commandFor">
    /// Resolves a shortcut id to its command, or null while the feature does not exist
    /// yet — those render disabled in the menus and get no binding here, so a greyed
    /// item and a dead key are always the same fact.
    /// </param>
    public static IReadOnlyList<(KeyGesture Gesture, ICommand Command)> For(
        Func<string, ICommand?> commandFor)
    {
        var bindings = new List<(KeyGesture, ICommand)>();

        foreach (var entry in ShortcutTable.All)
        {
            if (ShortcutGesture.For(entry.Id) is not { } gesture) continue;
            if (commandFor(entry.Id) is not { } command) continue;

            bindings.Add((gesture, command));
        }

        return bindings;
    }
}
