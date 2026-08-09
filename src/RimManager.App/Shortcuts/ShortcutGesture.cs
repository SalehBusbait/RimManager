using Avalonia.Input;

namespace RimManager.App.Shortcuts;

/// <summary>
/// The thin Avalonia adapter over <see cref="ShortcutTable"/>: turns a def into a
/// real <see cref="KeyGesture"/> so a menu item's displayed gesture and the window's
/// actual <c>KeyBinding</c> are built from one source and cannot disagree (guide §6).
/// </summary>
public static class ShortcutGesture
{
    /// <summary>True on macOS, where <see cref="KeyMod.Primary"/> means ⌘ rather than Ctrl.</summary>
    public static bool IsMac { get; } = OperatingSystem.IsMacOS();

    /// <summary>The gesture for a shortcut, or null when the entry is menu-only.</summary>
    public static KeyGesture? For(ShortcutDef def)
    {
        if (def.IsUnbound) return null;

        // On(IsMac), so the key that is BOUND is the same one the menus and the sheet
        // print. Reading def.Key directly here would advertise ⇧⌘Z on macOS and bind ⌘Y,
        // which is the display-only-gesture bug this class exists to prevent, inverted.
        var (mods, keyName) = def.On(IsMac);

        var modifiers = KeyModifiers.None;
        if (mods.HasFlag(KeyMod.Primary))
            modifiers |= IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (mods.HasFlag(KeyMod.Shift)) modifiers |= KeyModifiers.Shift;
        if (mods.HasFlag(KeyMod.Alt)) modifiers |= KeyModifiers.Alt;

        return Enum.TryParse<Key>(keyName, out var key) ? new KeyGesture(key, modifiers) : null;
    }

    /// <summary>The gesture for a shortcut id.</summary>
    public static KeyGesture? For(string id) => For(ShortcutTable.Get(id));

    /// <summary>The label a menu item shows on the right, e.g. "Ctrl+Shift+S" or "⌘⇧S".</summary>
    public static string Display(string id) => ShortcutFormatter.Format(ShortcutTable.Get(id), IsMac);
}
