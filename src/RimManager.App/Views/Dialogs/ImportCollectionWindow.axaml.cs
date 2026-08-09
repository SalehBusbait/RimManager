using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Dialogs;

public partial class ImportCollectionWindow : Window
{
    public ImportCollectionWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// One primary, two meanings — step 1 advances, step 2 commits. Only the second
    /// sets <see cref="ImportCollectionViewModel.Accepted"/>: Cancel, Esc and the ✕ all
    /// leave it false, so a dismissed wizard can never be mistaken for a decision. The
    /// same rule as the destructive confirm (<c>2i</c>-6).
    /// </summary>
    private void OnPrimary(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportCollectionViewModel vm) return;

        if (vm.IsStep1)
        {
            vm.ReviewCommand.Execute(null);
            return;
        }

        vm.Accepted = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
