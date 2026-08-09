using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace RimManager.App.Views.Dialogs;

public partial class ShortcutSheetWindow : Window
{
    public ShortcutSheetWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>Escape closes, like every non-modal reference window (T6 — this one
    /// and the diff ignored it; S-CONFLICTS's deviation note adds it to all four).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e is { Handled: false, Key: Key.Escape }) Close();
    }
}
