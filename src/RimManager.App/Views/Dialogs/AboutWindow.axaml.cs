using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Dialogs;

public partial class AboutWindow : Window
{
    public AboutWindow() => AvaloniaXamlLoader.Load(this);

    private AboutViewModel? Vm => DataContext as AboutViewModel;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnGitHub(object? sender, RoutedEventArgs e)
    {
        // Through the allowlisted URI launcher: https is one of the two schemes it permits.
        try { new RimManager.Storage.ShellUriLauncher().Launch(AboutViewModel.ProjectUrl); }
        catch (System.Exception) { /* no handler registered; nothing useful to say */ }
    }

    private async void OnCopyVersion(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Clipboard is null) return;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Text, Vm.VersionLine));
        await Clipboard.SetDataAsync(transfer);
    }
}
