using System.Text;

namespace RimManager.App.Shortcuts;

/// <summary>
/// Renders a <see cref="ShortcutDef"/> as the string a human reads.
/// <para>
/// Labels are identical on all three platforms; only the modifier glyph differs
/// (guide §6). macOS gets the glyph run ⌘⇧S with no separators — the platform
/// convention — everywhere else gets Ctrl+Shift+S.
/// </para>
/// Avalonia-free on purpose so it is unit-testable without a UI thread; the thin
/// adapter that turns a def into a real <c>KeyGesture</c> is
/// <see cref="ShortcutGesture"/>.
/// </summary>
public static class ShortcutFormatter
{
    /// <summary>Formats for display in a menu, the ⌘/ sheet or a tooltip.</summary>
    public static string Format(ShortcutDef def, bool isMac)
    {
        if (def.IsUnbound) return string.Empty;

        // Through On(), so an entry with a macOS-specific chord is PRINTED as the chord
        // that platform will actually accept. A sheet that advertises a key the OS does
        // not fire is worse than one that omits it.
        var (modifiers, key) = def.On(isMac);
        var sb = new StringBuilder();

        if (isMac)
        {
            // Order matches Apple's HIG: ⌃⌥⇧⌘ — we only ever use ⌥⇧⌘.
            if (modifiers.HasFlag(KeyMod.Alt)) sb.Append('⌥');
            if (modifiers.HasFlag(KeyMod.Shift)) sb.Append('⇧');
            if (modifiers.HasFlag(KeyMod.Primary)) sb.Append('⌘');
            sb.Append(KeyName(key, isMac: true));
            return sb.ToString();
        }

        if (modifiers.HasFlag(KeyMod.Primary)) sb.Append("Ctrl+");
        if (modifiers.HasFlag(KeyMod.Shift)) sb.Append("Shift+");
        if (modifiers.HasFlag(KeyMod.Alt)) sb.Append("Alt+");
        sb.Append(KeyName(key, isMac: false));
        return sb.ToString();
    }

    /// <summary>Formats by id — the form a menu item uses.</summary>
    public static string Format(string id, bool isMac) => Format(ShortcutTable.Get(id), isMac);

    /// <summary>
    /// The gesture string Avalonia's <c>KeyGesture.Parse</c> understands
    /// (<c>Ctrl+Shift+S</c>, <c>Alt+Up</c>, <c>F5</c>). Uses <c>Meta</c> on macOS,
    /// which is what maps to ⌘.
    /// </summary>
    public static string ToGestureString(ShortcutDef def, bool isMac)
    {
        if (def.IsUnbound) return string.Empty;

        var (modifiers, key) = def.On(isMac);
        var parts = new List<string>(4);
        if (modifiers.HasFlag(KeyMod.Primary)) parts.Add(isMac ? "Meta" : "Ctrl");
        if (modifiers.HasFlag(KeyMod.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyMod.Alt)) parts.Add("Alt");
        parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>Human-readable name for an Avalonia <c>Key</c> enum name.</summary>
    private static string KeyName(string key, bool isMac) => key switch
    {
        "Left" => "←",
        "Right" => "→",
        "Up" => "↑",
        "Down" => "↓",
        "Return" => isMac ? "⏎" : "Enter",
        "OemComma" => ",",
        "OemQuestion" => "/",
        "OemPeriod" => ".",
        // D0..D9 are the number-row keys; the digit is what the user sees.
        _ when key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1]) => key[1].ToString(),
        _ => key,
    };
}
