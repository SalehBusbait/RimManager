using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Dialogs;

/// <summary>
/// The two-up XML diff viewer (<c>3c</c>). The panes scroll together: an
/// unsynchronised two-up view is just two views, and the line alignment the diff
/// computes is the only reason they sit side by side.
/// </summary>
public partial class XmlDiffWindow : Window
{
    private ScrollViewer _left = null!;
    private ScrollViewer _right = null!;
    private bool _syncing;

    public XmlDiffWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _left = this.FindControl<ScrollViewer>("LeftScroller")!;
        _right = this.FindControl<ScrollViewer>("RightScroller")!;

        _left.ScrollChanged += (_, _) => Mirror(_left, _right);
        _right.ScrollChanged += (_, _) => Mirror(_right, _left);
    }

    private void Mirror(ScrollViewer from, ScrollViewer to)
    {
        // Guarded because writing Offset raises ScrollChanged on the other pane,
        // which would immediately write back and fight the user's wheel.
        if (_syncing) return;

        _syncing = true;
        to.Offset = to.Offset.WithY(from.Offset.Y);
        _syncing = false;
    }

    /// <summary>Escape closes, like every non-modal reference window (T6 — this one
    /// and the shortcut sheet ignored it).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e is { Handled: false, Key: Key.Escape }) Close();
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not XmlDiffViewModel vm || Clipboard is null) return;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Text, vm.AsText()));
        await Clipboard.SetDataAsync(transfer);
    }
}
