using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace RimManager.App.Views.Dialogs;

public partial class RuleEditorWindow : Window
{
    public RuleEditorWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>Escape closes, like every non-modal reference window (UI audit —
    /// this was the ONE dialog that ignored it).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e is { Handled: false, Key: Key.Escape }) Close();
    }
}
