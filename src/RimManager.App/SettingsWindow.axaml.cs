using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using RimManager.App.ViewModels;

namespace RimManager.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => AvaloniaXamlLoader.Load(this);

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    // OnCancel went with the commit bar. It was a bare Close(): a button labelled Cancel
    // beside one labelled Save promises a discard, and this window never held anything
    // back to discard. An orphaned OnClose went at the same time — a public handler no
    // markup referenced, the same shape as the three that were orphaned in R2a.

    /// <summary>
    /// Escape closes. It was <b>never wired</b>: this window has no <c>IsCancel</c> button
    /// (About and the dependency resolver both do), and Cancel being Tab-reachable was
    /// standing in for it — so removing Cancel would have left Settings closable by the
    /// title bar's ✕ alone.
    /// <para>
    /// Safe by construction rather than by luck: every edit here has already committed, so
    /// closing discards nothing. A handled key is left alone, which is what lets an open
    /// ComboBox take Escape for its own dropdown instead of losing the window.
    /// </para>
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e is { Handled: false, Key: Key.Escape })
        {
            e.Handled = true;
            Close();
        }
    }

    private async void OnBrowseGame(object? sender, RoutedEventArgs e) =>
        await BrowseAsync(p => { if (Vm is { } vm) vm.GameDir = p; });

    private async void OnBrowseConfig(object? sender, RoutedEventArgs e) =>
        await BrowseAsync(p => { if (Vm is { } vm) vm.ConfigDir = p; });

    private async void OnBrowseWorkshop(object? sender, RoutedEventArgs e) =>
        await BrowseAsync(p => { if (Vm is { } vm) vm.WorkshopDir = p; });

    private async void OnBrowseSteamCmd(object? sender, RoutedEventArgs e) =>
        await BrowseAsync(p => { if (Vm is { } vm) vm.SteamCmdDir = p; });

    /// <summary>
    /// The inline fix beside a failed check (<c>1c</c>). Dispatches on the action's own
    /// text, which is unambiguous because each verdict offers only the fix for its own
    /// field — <c>Locate…</c> is Workshop's, <c>Auto-detect</c> is the game's and the
    /// config's, and both of those want the same re-detection the toolbar button runs.
    /// <para>
    /// Lives in the view because "locate" means opening a folder picker, and a picker
    /// needs a top-level. An unrecognised action does nothing rather than throwing: it
    /// would mean a new verdict was added without a fix, which should not crash Settings.
    /// </para>
    /// </summary>
    private async void OnPathAction(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not PathCheck { Action: { } action }) return;

        switch (action)
        {
            case "Locate…":
                await BrowseAsync(p => { if (Vm is { } vm) vm.WorkshopDir = p; });
                break;

            case "Auto-detect":
                Vm?.AutoDetectCommand.Execute(null);
                break;
        }
    }

    // The theme preview cards and their Tapped handlers went with the accent picker
    // (T1): the interim list's rows are Buttons with commands, and the T4 gallery
    // brings back cards with a designed keyboard path (S-GALLERY).

    private async Task BrowseAsync(Action<string> set)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) set(path);
    }
}
