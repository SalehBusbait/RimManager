using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Shell;

public partial class ToolbarView : UserControl
{
    /// <summary>Where Apply sits at every width except below 900 (<c>2k</c>).</summary>
    // Shifted by one when Refresh took column 3. The search column's index below moved
    // with them — a stale index there silently caps the wrong column, which is the kind
    // of thing that only shows up as "the chips are in the wrong place at 1400px".
    private const int ApplyWideColumn = 5;
    private const int ApplyNarrowColumn = 11;
    private const int SearchColumn = 7;

    /// <summary>1a: search is "flex, max-width 420px". Below 900 the cap comes off so
    /// the slack goes into the field instead of stranding Apply mid-bar.</summary>
    private const double SearchWideCap = 420;

    private readonly StackPanel? _chips;
    private readonly ToggleButton? _collapse;
    private readonly Flyout? _flyout;
    private readonly Panel? _inlineHost;

    private bool _collapsed;
    private WindowLayout? _layout;

    /// <summary>
    /// Dismisses the modlist selector after any row in it acts.
    /// <para>
    /// A <see cref="Flyout"/> whose content is plain <see cref="Button"/>s does not close
    /// itself — only a <c>MenuFlyout</c>'s items do that. So switching a list left the
    /// selector hanging open over the whole-window load state for the length of the
    /// switch (capture mod settings, restore, rescan), listing the modlist it had already
    /// moved off, with a tick still on the old row.
    /// </para>
    /// <para>
    /// POSTED, not called inline, and that is the whole subtlety. Avalonia's
    /// <c>Button.OnClick</c> raises <c>Click</c> first and consults <c>Command</c>
    /// afterwards; hiding the flyout inside the handler tears the popup's visual tree
    /// down, the templated row's bindings clear with it, and <c>Command</c> reads null by
    /// the time the button looks — so the flyout closed and the modlist never switched.
    /// Driving the app is what caught that; it builds and passes either way.
    /// </para>
    /// </summary>
    private void OnModlistFlyoutAction(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(
            () => this.FindControl<Button>("InstanceSelector")?.Flyout?.Hide(),
            DispatcherPriority.Background);

    public ToolbarView()
    {
        AvaloniaXamlLoader.Load(this);

        _chips = this.FindControl<StackPanel>("FilterChips");
        _collapse = this.FindControl<ToggleButton>("FilterCollapse");
        _flyout = _collapse?.Flyout as Flyout;
        _inlineHost = _chips?.Parent as Panel;

        // Ctrl+F (UI audit — printed on the ⌘/ sheet since R7, bound to nothing):
        // the hub raises, this view owns the box, so this view answers.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SearchFocusRequested += () => this.FindControl<TextBox>("SearchBox")?.Focus();
        };

        // The toolbar has no size of its own to react to — it stretches — so the
        // breakpoints come from LayoutWidth, the same number the mod-info drawer, the
        // menu bar and the row columns are decided from. Two independent measurements
        // of "how wide is the window" is how two surfaces end up disagreeing about
        // which layout is in force — and this file did exactly that until the R9 hand
        // review: it read WindowWidth while everything else had moved to LayoutWidth,
        // so at any UI scale other than 100% the toolbar was one layout behind. At
        // 150% on an 1180px window the lists went segmented while the segmented switch
        // — the only control that reaches the other list — stayed hidden.
        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    private void Subscribe()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.LayoutWidth)) Apply(vm);
        };
        Apply(vm);
    }

    /// <summary>
    /// Both breakpoints, in one place because they are one continuum: at 1150 the
    /// filter chips collapse into "Filters N ▾" (which is itself then hidden at 900),
    /// and at 900 the toolbar keeps only the segmented switch, search, ⋯ and Apply.
    /// </summary>
    private void Apply(MainWindowViewModel vm)
    {
        var layout = Breakpoints.For(vm.LayoutWidth);
        if (layout == _layout) return;
        _layout = layout;

        CollapseChips(layout != WindowLayout.Full);
        Narrow(layout == WindowLayout.Segmented);
    }

    /// <summary>
    /// 2k · "the toolbar collapses its filter chips into a single Filters N ▾ button".
    /// The chips are MOVED into the flyout and turned vertical, never duplicated: a
    /// second copy of four filter controls would drift from the first, and the drift
    /// would be invisible because only one is ever on screen.
    /// </summary>
    private void CollapseChips(bool collapsed)
    {
        if (_chips is null || _collapse is null || _flyout is null || _inlineHost is null) return;
        if (collapsed == _collapsed) return;

        _collapsed = collapsed;
        _collapse.IsVisible = collapsed;

        if (collapsed)
        {
            _inlineHost.Children.Remove(_chips);
            _chips.Orientation = Orientation.Vertical;
            _chips.Spacing = 4;
            _chips.HorizontalAlignment = HorizontalAlignment.Stretch;
            _flyout.Content = _chips;
        }
        else
        {
            _flyout.Content = null;
            _chips.Orientation = Orientation.Horizontal;
            _chips.HorizontalAlignment = HorizontalAlignment.Left;
            // Insert at 0, NOT Add. The host is a horizontal StackPanel, where child
            // order is layout order, and the chips are its FIRST child — Filters ▾ and
            // Columns ▾ follow them. Appending put the four chips back to the right of
            // Columns ▾, so widening the window past 1150 after it had narrowed left
            // the toolbar permanently reordered. (The mod-info pane re-parents the same
            // way and is safe only because its host is a Grid with an explicit column.)
            if (!_inlineHost.Children.Contains(_chips)) _inlineHost.Children.Insert(0, _chips);
            _collapse.IsChecked = false;
        }
    }

    /// <summary>
    /// 2k · below 900 "the toolbar keeps only search, an overflow ⋯ and Apply", plus
    /// the segmented list switch that takes the instance selector's place.
    /// </summary>
    private void Narrow(bool narrow)
    {
        void Show(string name, bool visible)
        {
            if (this.FindControl<Control>(name) is { } control) control.IsVisible = visible;
        }

        Show("SegmentedSwitch", narrow);
        Show("InstanceSelector", !narrow);
        Show("InstanceDivider", !narrow);
        Show("SortButton", !narrow);
        Show("RefreshButton", !narrow);
        Show("UndoRedo", !narrow);
        Show("SearchDivider", !narrow);
        Show("OverflowButton", narrow);

        // The chips are already in the Filters flyout by this width; below 900 the
        // button that opens it goes into ⋯ territory too, so it is hidden outright
        // rather than left as a tenth control on a toolbar the design cuts to four.
        //
        // ColumnsButton is no longer here to hide (N1): the inactive pane sizes its own
        // columns from its own width now.
        Show("FilterCollapse", !narrow && _collapsed);

        if (this.FindControl<SplitButton>("ApplyButton") is { } apply)
            Grid.SetColumn(apply, narrow ? ApplyNarrowColumn : ApplyWideColumn);

        // Set, not bound: a ColumnDefinition is not in the logical tree, so a binding
        // on MaxWidth resolves against nothing and fails in silence.
        if (this.FindControl<Grid>("ToolbarGrid") is { } grid && grid.ColumnDefinitions.Count > SearchColumn)
            grid.ColumnDefinitions[SearchColumn].MaxWidth = narrow ? double.PositiveInfinity : SearchWideCap;
    }
}
