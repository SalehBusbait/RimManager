using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Dialogs;

/// <summary>The S-RWLIST task dialog (NF-10). See the markup comment for the shape.</summary>
public partial class RwListOfferWindow : Window
{
    public RwListOfferWindow() => AvaloniaXamlLoader.Load(this);

    private RwListOfferViewModel? Vm => DataContext as RwListOfferViewModel;

    private void OnNotNow(object? sender, RoutedEventArgs e) => Close();

    private void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.Accepted = true;
        Close();
    }
}
