using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace RimManager.App.Views.Dialogs;

public partial class DependencyResolverWindow : Window
{
    public DependencyResolverWindow() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
