using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace RimManager.App.Views.Dialogs;

/// <summary>The full mod description (O3). See the markup comment for the shape.</summary>
public partial class DescriptionWindow : Window
{
    public DescriptionWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>Escape closes, like every non-modal reference window.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
