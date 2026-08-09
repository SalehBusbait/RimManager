using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Dialogs;

public partial class DestructiveConfirmWindow : Window
{
    public DestructiveConfirmWindow() => AvaloniaXamlLoader.Load(this);

    private DestructiveConfirmViewModel? Vm => DataContext as DestructiveConfirmViewModel;

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Vm?.Confirm();
        Close();
    }

    /// <summary>
    /// The <see cref="Confirmer"/> the view models are handed. Lives here because a modal
    /// needs a parent window, and view models in this project stay constructible without
    /// one.
    /// <para>
    /// Closing by any route other than the primary button — Cancel, Escape, the title
    /// bar's ✕ — leaves <c>Confirmed</c> false, so a dismissed dialog can never be read
    /// as consent.
    /// </para>
    /// </summary>
    public static Confirmer For(Window owner) => async request =>
    {
        var vm = new DestructiveConfirmViewModel(request);
        await new DestructiveConfirmWindow { DataContext = vm }.ShowDialog(owner);
        return vm.Result;
    };
}
