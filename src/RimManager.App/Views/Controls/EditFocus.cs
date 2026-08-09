using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using Avalonia.Threading;

namespace RimManager.App.Views.Controls;

/// <summary>
/// Puts the caret in a text box the moment it appears, and selects what is already there.
/// <para>
/// An inline editor that opens unfocused is an editor the user has to find and click before
/// they can use it — which is what the separator rename did: creating a separator revealed
/// its name box and then left the keyboard pointed somewhere else, so the obvious next
/// keystroke went nowhere.
/// </para>
/// <para>
/// An attached property rather than code-behind because the box lives inside a
/// <c>DataTemplate</c> in a virtualising list: there is no view class to hang a handler on,
/// and the container is recycled. <c>Loaded</c> is no use either — it fires when the row is
/// realised, not when editing starts, and the two are usually minutes apart.
/// </para>
/// </summary>
public static class EditFocus
{
    public static readonly AttachedProperty<bool> FocusOnShowProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("FocusOnShow", typeof(EditFocus));

    public static bool GetFocusOnShow(TextBox box) => box.GetValue(FocusOnShowProperty);

    public static void SetFocusOnShow(TextBox box, bool value) => box.SetValue(FocusOnShowProperty, value);

    static EditFocus() => FocusOnShowProperty.Changed.AddClassHandler<TextBox>(OnFocusOnShowChanged);

    private static void OnFocusOnShowChanged(TextBox box, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;

        // The subscription holds only the box, and the box holds the subscription, so the
        // pair is collected together when the container is recycled.
        box.GetObservable(Visual.IsVisibleProperty).Subscribe(new AnonymousObserver<bool>(visible =>
        {
            if (!visible) return;

            // Posted, not called inline. The box becomes visible during a layout pass, and
            // focusing a control that has not been arranged yet silently does nothing —
            // the failure looks exactly like not having written this at all.
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!box.IsVisible) return;
                    box.Focus();
                    box.SelectAll();
                },
                DispatcherPriority.Input);
        }));
    }
}
