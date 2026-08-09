using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Dialogs;

/// <summary>The S-ORDERDIFF task dialog. See the markup comment for the shape.</summary>
public partial class OrderDiffWindow : Window
{
    public OrderDiffWindow() => AvaloniaXamlLoader.Load(this);

    private OrderDiffViewModel? Vm => DataContext as OrderDiffViewModel;

    private void OnKeepMine(object? sender, RoutedEventArgs e) => Close();

    private void OnTakeTheirs(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.Accepted = true;
        Close();
    }
}
