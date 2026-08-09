using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.FirstRun;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow() => AvaloniaXamlLoader.Load(this);

    private FirstRunViewModel? Vm => DataContext as FirstRunViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Vm is { } vm) vm.CloseRequested += Close;
    }

    private void OnPalette(object? sender, RoutedEventArgs e)
    {
        // The index is parsed here rather than passed as a CommandParameter: that is a
        // *string* in XAML, which a command taking an int silently refuses — the bug
        // that killed all six tag swatches in R6.
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index) && Vm is { } vm)
        {
            vm.PaletteIndex = index;
        }
    }

    private async void OnBrowseGame(object? sender, RoutedEventArgs e) =>
        await BrowseAsync(p => { if (Vm is { } vm) vm.GameDir = p; });

    private async void OnBrowseConfig(object? sender, RoutedEventArgs e) =>
        await BrowseAsync(p => { if (Vm is { } vm) vm.ConfigDir = p; });

    // OnBrowseLocalMods is GONE with its Browse button (UI audit): the folder is
    // derived from the game folder — the value it collected was never read.

    private async Task BrowseAsync(Action<string> set)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) set(path);
    }
}
