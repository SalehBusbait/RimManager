using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using Avalonia.Automation;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using RimManager.App.Shortcuts;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;

namespace RimManager.App;

public partial class MainWindow : Window
{
    private const double DragThreshold = 4;

    // In-process drag payload: the row being dragged (Avalonia 12 DataTransfer API).
    private static readonly DataFormat<RowViewModel> RowFormat =
        DataFormat.CreateInProcessFormat<RowViewModel>("rimmanager-row");

    // Edge auto-scroll (3a). "Mandatory at 200+ rows" — and a real list is 200+.
    // A timer rather than per-DragOver scrolling, because DragOver stops firing the
    // moment the pointer stops moving, and holding still at the edge is exactly the
    // gesture people use to travel a long way.
    private const double AutoScrollMargin = 24;
    private const double AutoScrollPixelsPerSecond = 600;
    private const double AutoScrollTickSeconds = 1.0 / 60;

    private DispatcherTimer? _autoScroll;
    private ScrollViewer? _autoScrollViewer;
    private double _autoScrollDirection;

    private PointerPressedEventArgs? _pressArgs;
    private RowViewModel? _pressRow;
    private ListBox? _pressList;
    private string? _pressPane;
    private Point _pressPosition;
    private bool _dragging;

    private ListBox _activeList = null!;
    private ListBox _inactiveList = null!;

    // --- window memory (O17) --------------------------------------------------

    /// <summary>
    /// The last bounds the window had while NOT maximised.
    /// <para>
    /// Tracked by hand because a maximised window's own Position and size are the
    /// maximised ones, and saving those as if they were normal is how un-maximising
    /// after a restart snaps to a size nobody chose. Avalonia exposes no restore-bounds
    /// property, so this records them as they happen.
    /// </para>
    /// </summary>
    private PlacementRect? _normalBounds;

    private bool _layoutSaveStarted;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Both are needed: a move raises PositionChanged only, a resize Resized only.
        //
        // POSTED, not called inline. Maximising raises Resized with the maximised size
        // while WindowState still reads Normal — the platform reports the new geometry
        // before Avalonia refreshes the property — so an inline capture recorded the
        // maximised rectangle AS the restored bounds. Driving caught it twice: the
        // saved "normal" size was the full screen. A background-priority post runs
        // after the state has settled, and then the Normal check is true.
        PositionChanged += (_, _) => QueueBoundsCapture();
        Resized += (_, _) => QueueBoundsCapture();
        _inactiveList = this.FindControl<ListBox>("InactiveList")!;
        _activeList = this.FindControl<ListBox>("ActiveList")!;
        Wire(_inactiveList);
        Wire(_activeList);
        this.FindControl<Button>("ActivateBtn")!.Click += (_, _) => MoveSelection(activate: true);
        this.FindControl<Button>("DeactivateBtn")!.Click += (_, _) => MoveSelection(activate: false);

        _activeList.SelectionChanged += OnRowSelectionChanged;
        _inactiveList.SelectionChanged += OnRowSelectionChanged;
        // "double-click on any row that names a mod reveals that mod in the load
        // order" (SCREENS.md, shared dock rules). Single-click only selects — it has
        // to, or reading a warning would keep yanking the list out from under you.
        this.FindControl<ListBox>("WarningsList")!.DoubleTapped += OnWarningTapped;

        // N6b · double-clicking an active mod opens its conflict window. The ACTIVE
        // list only: an inactive mod is never loaded, so its window would always say
        // "nothing contested" — a true sentence that answers no question.
        _activeList.DoubleTapped += OnActiveRowDoubleTapped;

        // N1 · the inactive pane sizes its own columns from its OWN width, because the
        // splitter beside it is user-draggable and the window's width therefore says
        // nothing about how much room this list has. Fires on both causes — window
        // resize and splitter drag — because both change this Border.
        //
        // No scale division here, unlike WindowWidth below: this Border lives inside
        // the UI-scale LayoutTransformControl, so its bounds are already layout units,
        // which is the space the column thresholds are written in.
        if (this.FindControl<Border>("InactivePane") is { } inactivePane)
        {
            inactivePane.SizeChanged += (_, e) =>
            {
                if (DataContext is not MainWindowViewModel vm) return;
                vm.InactivePaneWidth = e.NewSize.Width;
                // O17: and tell it the splitter positions, so a layout save triggered
                // by anything else carries current pane widths rather than stale ones.
                ReportPaneWidthsTo(vm);
            };
        }

        // The dock's "max 50% of window" ceiling is only real if the view model can
        // see the window. Pushed rather than bound because the view model must stay
        // constructible without a window (that is what makes its logic testable).
        SizeChanged += (_, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.WindowHeight = e.NewSize.Height;
                vm.WindowWidth = e.NewSize.Width;
            }

            // Not e.NewSize.Width: with UI scale in play the LAYOUT gets a different
            // width from the window, and the breakpoints are about the layout.
            ApplyAdaptiveLayout(
                DataContext is MainWindowViewModel v ? v.LayoutWidth : e.NewSize.Width);
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (ReferenceEquals(vm, _wired)) return;   // subscribing twice doubles every handler
            _wired = vm;

            vm.WindowWidth = Bounds.Width;
            InstallKeyBindings(vm);
            vm.UiScaleChanged += ApplyUiScale;
            ApplyUiScale(vm.UiScaleFactor);
            vm.LayoutWidthChanged += ApplyAdaptiveLayout;
            vm.InfoPaneFocusRequested += () => _infoPane?.Focus();
            vm.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(MainWindowViewModel.ShowInfoDrawer):
                        RehomeFocusAfterDrawer(vm.ShowInfoDrawer);
                        break;
                    case nameof(MainWindowViewModel.SegmentShowsInactive):
                        ApplySegment(vm);
                        break;
                }
            };
            ApplyAdaptiveLayout(vm.LayoutWidth);
        };
    }



    private MainWindowViewModel? _wired;

    /// <summary>
    /// 2g · UI scale. Set rather than bound: a <c>ScaleTransform</c> is not in the
    /// logical tree, so a binding onto one resolves against nothing and fails in the
    /// silence this codebase keeps paying for.
    /// <para>
    /// Cleared to null at 100% rather than left as an identity transform, so the common
    /// case measures and renders through no transform at all.
    /// </para>
    /// </summary>
    private void ApplyUiScale(double factor)
    {
        if (this.FindControl<LayoutTransformControl>("UiScaleHost") is not { } host) return;

        host.LayoutTransform = Math.Abs(factor - 1) < 0.001
            ? null
            : new ScaleTransform(factor, factor);
    }

    /// <summary>
    /// Every key binding in the window, generated from <see cref="ShortcutTable"/>.
    /// <para>
    /// They used to be eleven hand-written entries in the markup against a table of
    /// forty-six. A <c>MenuItem.InputGesture</c> only draws its gesture — it invokes
    /// nothing — so a shortcut with a row in the table, a label in a menu and no
    /// binding here printed itself everywhere and did nothing when pressed.
    /// </para>
    /// </summary>
    private void InstallKeyBindings(MainWindowViewModel vm)
    {
        KeyBindings.Clear();

        foreach (var (gesture, command) in ShortcutBindings.For(vm.CommandFor))
            KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
    }

    // --- 2k · the two breakpoints --------------------------------------------

    private Border? _infoPane;
    private Border? _infoDrawerHost;
    private Grid? _paneGrid;
    private GridSplitter? _infoSplitter;
    private bool _infoIsDrawer;

    /// <summary>
    /// Closing the drawer hides the element that was focused — its own ✕ — and focus
    /// goes nowhere. Key bindings are dispatched from the focused element upwards, so
    /// the window then stops seeing Ctrl+3 and the drawer cannot be reopened from the
    /// keyboard at all. Focus goes back to the active list, but only when it was
    /// inside the drawer: closing with Esc from the search box must not yank it.
    /// </summary>
    private void RehomeFocusAfterDrawer(bool open)
    {
        if (open || _infoPane is null) return;

        var focused = FocusManager?.GetFocusedElement() as Visual;
        var wasInside = focused is not null
            && (ReferenceEquals(focused, _infoPane) || focused.GetVisualAncestors().Contains(_infoPane));

        if (wasInside || focused is null) _activeList.Focus();
    }

    /// <summary>
    /// Below 1150px (<c>2k</c>) the mod-info pane MOVES into the drawer host, and back
    /// when the window widens. The same Border instance travels, because the design
    /// promises "same view, same VM instance" and an intact scroll position on the way
    /// back — two controls bound to one view model would give the same data in a fresh
    /// visual tree, scrolled to the top.
    /// <para>
    /// The grid's columns are set here rather than bound: a <c>ColumnDefinition</c> is
    /// not in the logical tree, so a binding on its Width resolves against nothing and
    /// fails in silence.
    /// </para>
    /// </summary>
    private void ApplyAdaptiveLayout(double width)
    {
        _infoPane ??= this.FindControl<Border>("InfoPane");
        _infoDrawerHost ??= this.FindControl<Border>("InfoDrawerHost");
        _infoSplitter ??= this.FindControl<GridSplitter>("InfoPaneSplitter");
        if (_infoPane is null || _infoDrawerHost is null) return;

        _paneGrid ??= _infoPane.Parent as Grid;

        ApplySegmentedLayout(width);

        var wantsDrawer = Breakpoints.InfoIsOverlay(width);
        if (wantsDrawer == _infoIsDrawer) return;

        // The offset survives the move only if we carry it: detaching resets the
        // scroll viewer's extent, and it re-measures at the top.
        var scroller = _infoPane.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroller?.Offset ?? default;

        if (wantsDrawer)
        {
            _paneGrid?.Children.Remove(_infoPane);
            _infoDrawerHost.Child = _infoPane;
        }
        else
        {
            _infoDrawerHost.Child = null;
            if (_paneGrid is not null && !_paneGrid.Children.Contains(_infoPane))
                _paneGrid.Children.Add(_infoPane);
        }

        // The third column and its splitter give their width back to the lists — which
        // is the whole point of the breakpoint. 2k: "the load order is the last thing
        // to lose space."
        if (_paneGrid is { ColumnDefinitions.Count: >= 5 })
        {
            _paneGrid.ColumnDefinitions[3].Width = new GridLength(wantsDrawer ? 0 : 6);
            _paneGrid.ColumnDefinitions[4].Width = new GridLength(wantsDrawer ? 0 : 344, GridUnitType.Pixel);
        }

        if (_infoSplitter is not null) _infoSplitter.IsVisible = !wantsDrawer;
        _infoIsDrawer = wantsDrawer;

        if (scroller is not null)
            Dispatcher.UIThread.Post(() => scroller.Offset = offset, DispatcherPriority.Loaded);
    }

    private bool _segmented;

    /// <summary>
    /// 2k · breakpoint 2. Below 900px "the two lists become one segmented view". They
    /// become one by SHOWING one: both panes already carry their own row templates,
    /// footers, drag handling and empty states, and a third list built for this width
    /// would be a third thing to keep in step with the other two — which is exactly how
    /// the Activity panel and the log file drifted apart.
    /// </summary>
    private void ApplySegmentedLayout(double width)
    {
        var segmented = Breakpoints.For(width) == WindowLayout.Segmented;
        if (segmented == _segmented) return;

        _segmented = segmented;

        // The 24px narrow row height is NOT written here. Density has exactly one
        // writer of RmRowHeight (MainWindowViewModel.ApplyDensity), and a second one
        // would disagree with it the first time a resize and a density change arrived
        // in the wrong order — the same shape as the two theme stores in R6.
        if (DataContext is MainWindowViewModel vm) ApplySegment(vm);
    }

    /// <summary>
    /// Which of the two panes the segmented switch is showing. Above the breakpoint
    /// both are visible and this does nothing — the switch is not on screen to have
    /// been touched.
    /// </summary>
    /// <summary>
    /// The inactive pane's share of the two lists, measured off <c>1a</c> at 1440:
    /// inactive 300, active 790. It is declared in MainWindow.axaml too; both have to
    /// agree, so the number lives here and the markup carries the same literal with a
    /// note saying why.
    /// </summary>
    private const double InactivePaneShare = 0.38;

    private GridLength[]? _paneSplit;

    // ================= window memory (O17) =================

    private void QueueBoundsCapture() =>
        Dispatcher.UIThread.Post(RememberNormalBounds, DispatcherPriority.Background);

    private void RememberNormalBounds()
    {
        if (WindowState == WindowState.Normal && Width > 0 && Height > 0)
            _normalBounds = new PlacementRect(Position.X, Position.Y, Width, Height);

        // Pushed to the view model on every move and resize, not only on close.
        //
        // Reporting only at close was wrong and driving caught it: layout saves happen
        // DURING the session too — dragging the dock queues one — and such a save built
        // its state from a view model that had never been told where the window was, so
        // it wrote nulls over geometry the previous close had saved correctly. The
        // window then reopened at the markup literal. This is an in-memory assignment,
        // so it costs nothing and adds no writes.
        //
        // _normalBounds is passed AS IS, null included, and there is no falling back to
        // the current rectangle. That fallback was the second defect driving found, and
        // it was self-feeding: a window restored already maximised has never been Normal
        // this session, so the fallback saved the MAXIMISED rectangle as the restored
        // bounds, and the next launch read those back — un-maximising would then land on
        // a full-screen-sized window nobody had chosen. Reporting null keeps the last
        // real bounds instead.
        if (DataContext is MainWindowViewModel vm)
            vm.ReportWindowGeometry(_normalBounds, WindowState == WindowState.Maximized);
    }

    private void ReportPaneWidthsTo(MainWindowViewModel vm)
    {
        if (_paneGrid is not { ColumnDefinitions.Count: >= 5 }) return;

        // The ACTUAL widths, not the declared GridLengths: column 0 is a star in the
        // markup, and a star is not a number anyone can restore.
        vm.ReportPaneWidths(
            _paneGrid.ColumnDefinitions[0].ActualWidth,
            _paneGrid.ColumnDefinitions[4].ActualWidth);
    }

    /// <summary>
    /// Puts the saved geometry on the window BEFORE it is shown — called from the
    /// composition root, because the window is constructed and shown long before the
    /// view model's InitializeAsync runs and a resize after that is a visible jump.
    /// </summary>
    public void RestoreGeometry(LayoutState layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var screens = Screens?.All
            .Select(s => new PlacementRect(
                s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height))
            .ToList() ?? [];

        // The saved size is in DIPs and the saved position in physical pixels, so the
        // overlap test converts. A position that is on no screen at all yields no
        // scaling and no overlap, which is the same answer either way.
        var scale = layout.WindowX is { } sx && layout.WindowY is { } sy
            ? Screens?.ScreenFromPoint(new PixelPoint((int)sx, (int)sy))?.Scaling ?? 1.0
            : 1.0;

        var placed = WindowPlacement.Restore(
            layout.WindowX, layout.WindowY,
            layout.WindowWidth * scale, layout.WindowHeight * scale, screens);

        if (placed is { } rect && layout is { WindowWidth: { } w, WindowHeight: { } h })
        {
            Width = w;
            Height = h;
            Position = new PixelPoint((int)rect.X, (int)rect.Y);
            WindowStartupLocation = WindowStartupLocation.Manual;
            _normalBounds = new PlacementRect(rect.X, rect.Y, w, h);
        }
        else if (WindowPlacement.RestoreSizeOnly(
                     layout.WindowWidth * scale, layout.WindowHeight * scale, screens) is { } size)
        {
            // The monitor it lived on is gone, but the SIZE was still their choice.
            // Centred, at the size they picked, beats centred at the markup literal.
            Width = size.Width / scale;
            Height = size.Height / scale;
        }

        // Applied after the bounds, and on purpose: setting Maximized first and then a
        // size writes the size into the maximised state on some backends.
        //
        // _normalBounds was seeded above from the SAVED bounds even on this path, and it
        // has to be: a window that opens maximised is never in a Normal state for the
        // tracker to observe, so without the seed it would have no restored bounds to
        // report all session — and un-maximising would fall back to the markup default
        // rather than to the size the user had before they maximised.
        if (layout.WindowMaximised) WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Hands the view model everything only the view can measure, immediately before
    /// the layout is written.
    /// </summary>
    /// <summary>A final report before the layout is written on the way out.</summary>
    private void ReportGeometryTo(MainWindowViewModel vm)
    {
        RememberNormalBounds();
        ReportPaneWidthsTo(vm);
    }

    /// <summary>
    /// Saves the layout on the way out, then closes for real.
    /// <para>
    /// Geometry is deliberately NOT saved on every move: <c>layout.json</c> writes a
    /// timestamped backup on every save, and a window drag is a stream of events —
    /// unthrottled that is the backup churn <c>PruneBackups</c> was written for. The
    /// close is the one moment the final answer is known.
    /// </para>
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_layoutSaveStarted && DataContext is MainWindowViewModel vm)
        {
            _layoutSaveStarted = true;
            ReportGeometryTo(vm);
            e.Cancel = true;
            _ = SaveLayoutThenCloseAsync(vm);
            return;
        }

        base.OnClosing(e);
    }

    private async Task SaveLayoutThenCloseAsync(MainWindowViewModel vm)
    {
        try
        {
            await vm.SaveLayoutNowAsync();
        }
        catch (Exception)
        {
            // A failed layout save must never trap the user in the app.
        }

        Close();
    }

    /// <summary>
    /// Puts restored splitter positions on screen. The widths live in the markup as
    /// literals that a markup test pins, so the restore overwrites at runtime rather
    /// than editing the declaration.
    /// </summary>
    private void ApplyPaneWidths(double inactive, double info)
    {
        if (_paneGrid is not { ColumnDefinitions.Count: >= 5 }) return;
        if (inactive <= 0 || info <= 0) return;

        _paneGrid.ColumnDefinitions[0].Width = new GridLength(inactive, GridUnitType.Pixel);
        _paneGrid.ColumnDefinitions[4].Width = new GridLength(info, GridUnitType.Pixel);
    }

    private void ApplySegment(MainWindowViewModel vm)
    {
        if (_paneGrid is not { ColumnDefinitions.Count: >= 5 }) return;

        if (!_segmented)
        {
            // Give the user's splitter position back rather than resetting to 50/50.
            // Same discipline as the drawer's scroll offset: crossing a breakpoint is
            // not an event the user asked for, so it must not throw their layout away.
            if (_paneSplit is { Length: 3 })
            {
                for (var i = 0; i < 3; i++) _paneGrid.ColumnDefinitions[i].Width = _paneSplit[i];
                _paneSplit = null;
            }
            else
            {
                // The same ratio the markup declares, measured off 1a: the load order
                // gets about 2.6x the inactive pane. Restoring 1:1 here would quietly
                // undo that the first time someone crossed the breakpoint.
                _paneGrid.ColumnDefinitions[0].Width = new GridLength(InactivePaneShare, GridUnitType.Star);
                _paneGrid.ColumnDefinitions[1].Width = new GridLength(6);
                _paneGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }

            if (this.FindControl<GridSplitter>("PaneSplitter") is { } wide) wide.IsVisible = true;
            return;
        }

        _paneSplit ??=
        [
            _paneGrid.ColumnDefinitions[0].Width,
            _paneGrid.ColumnDefinitions[1].Width,
            _paneGrid.ColumnDefinitions[2].Width,
        ];

        var showInactive = vm.SegmentShowsInactive;
        _paneGrid.ColumnDefinitions[0].Width =
            showInactive ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _paneGrid.ColumnDefinitions[1].Width = new GridLength(0);
        _paneGrid.ColumnDefinitions[2].Width =
            showInactive ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        if (this.FindControl<GridSplitter>("PaneSplitter") is { } splitter) splitter.IsVisible = false;
    }

    private static List<ModRowViewModel> SelectedMods(ListBox list)
    {
        var result = new List<ModRowViewModel>();
        if (list.SelectedItems is null) return result;
        foreach (var item in list.SelectedItems)
            if (item is ModRowViewModel mod) result.Add(mod);
        return result;
    }

    private void MoveSelection(bool activate)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (activate) vm.ActivateMods(SelectedMods(_inactiveList));
        else vm.DeactivateMods(SelectedMods(_activeList));
    }

    /// <summary>
    /// Fills the context state just before the menu opens (2i-8).
    /// <para>
    /// A right-click on a row OUTSIDE the current selection selects that row first, the
    /// way every list does — otherwise "Delete from disk" would act on rows the user is
    /// not looking at, which is the one mistake this menu must not make.
    /// </para>
    /// </summary>
    private void OnRowContextOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not ContextMenu menu) return;

        // The pane comes from the menu's own Tag rather than a tree walk: a ContextMenu
        // is a popup root, so its parent is not the ListBox it belongs to.
        var list = menu.Tag as string == "active" ? _activeList : _inactiveList;

        var selected = SelectedMods(list);
        if (selected.Count == 0)
        {
            e.Cancel = true;   // nothing to act on; a menu of disabled items helps nobody
            return;
        }

        vm.PrepareRowContext(selected, fromActivePane: ReferenceEquals(list, _activeList));
    }

    /// <summary>N6b · a double-click on an active MOD row opens its conflict window.
    /// Separators fall through the pattern and do nothing.</summary>
    private void OnActiveRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>()?.DataContext
            is not ModRowViewModel row) return;

        vm.OpenModConflictsCommand.Execute(row);
    }

    private void OnRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || DataContext is not MainWindowViewModel vm) return;

        // Feeds "Sort selection only 3" in the Sort flyout and the pane footers.
        vm.SelectionCount = list.SelectedItems?.Count ?? 0;

        // The two lists hold independent selections and the hub keeps them apart:
        // the active one feeds "+ Separator" and the Edit menu's deactivate/move,
        // the inactive one feeds Edit ▸ Activate selected (UI audit — the menu
        // command needs to know what the inactive list holds).
        if (ReferenceEquals(list, _activeList))
        {
            vm.SetActiveSelection(
                list.SelectedItems?.OfType<RowViewModel>().ToList() ?? []);
        }
        else if (ReferenceEquals(list, _inactiveList))
        {
            vm.SetInactiveSelection(
                list.SelectedItems?.OfType<RowViewModel>().ToList() ?? []);
        }

        if (list.SelectedItem is ModRowViewModel row) vm.SelectMod(row.Mod);
    }

    private void OnWarningTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var entry = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>()?.DataContext
            as WarningEntry;
        if (entry is null || entry.IsGroupHeader || entry.Subject is not { } id) return;

        var row = vm.SelectByPackageId(id);
        if (row is null) return;

        var list = vm.ActiveRows.Contains(row) ? _activeList : _inactiveList;
        list.SelectedItem = row;
        list.ScrollIntoView(row);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is not MainWindowViewModel vm) return;

        WireActivityFollow(vm);

        // 3c is non-modal: Show, not ShowDialog. It is a reference you keep open
        // while reordering the resolution chain behind it.
        vm.XmlDiffRequested += diff =>
            new Views.Dialogs.XmlDiffWindow { DataContext = diff }.Show(this);

        // N6b, the same shape: a reference beside the lists, never a modal.
        vm.ModConflictsRequested += conflicts =>
            new Views.Dialogs.ModConflictsWindow { DataContext = conflicts }.Show(this);

        // N8: the image the info pane's crop is a crop of — a reference, so Show.
        vm.ImageViewerRequested += image =>
            new Views.Dialogs.ImageViewerWindow { DataContext = image }.Show(this);

        // O3: the description the pane clamps — a reference too, so Show.
        vm.DescriptionViewerRequested += description =>
            new Views.Dialogs.DescriptionWindow { DataContext = description }.Show(this);

        // O17: the saved splitter positions, once, when ApplyLayout runs.
        vm.PaneWidthsRequested += ApplyPaneWidths;

        // 3d is a reference you keep open while trying the keys it lists, so Show,
        // not ShowDialog.
        // 2i-4 is modal: it activates mods and downloads files.
        vm.DependencyResolverRequested += async resolver =>
        {
            await new Views.Dialogs.DependencyResolverWindow { DataContext = resolver }
                .ShowDialog(this);

            if (!resolver.AnythingResolved) return;
            if (resolver.SortAfterResolving) vm.SortCommand.Execute(null);
            else vm.Validate();
        };

        // 2i-3 is modal: the strategy it records can deactivate a hundred mods. Step 2
        // is the Collection dock tab, which CompleteCollectionImport opens onto.
        vm.ImportCollectionRequested += async wizard =>
        {
            await new Views.Dialogs.ImportCollectionWindow { DataContext = wizard }
                .ShowDialog(this);
            vm.CompleteCollectionImport(wizard);
        };

        // NF-10 · the S-RWLIST offer: modal in the confirm family — it exists to
        // collect one consent, and every route out marks the offer seen.
        vm.RwListOfferRequested += async dialog =>
        {
            await new Views.Dialogs.RwListOfferWindow { DataContext = dialog }
                .ShowDialog(this);
            await vm.CompleteRwListOfferAsync(dialog);
        };

        // S-ORDERDIFF · the review the game-moved strip and the drift zone open:
        // modal, confirm family — only "Take theirs" changes anything.
        vm.OrderDiffRequested += async dialog =>
        {
            await new Views.Dialogs.OrderDiffWindow { DataContext = dialog }
                .ShowDialog(this);
            await vm.CompleteOrderDiffAsync(dialog);
        };

        // 2i-5 is NON-modal: it is a reference kept open beside the load order.
        vm.RuleEditorRequested += editor =>
            new Views.Dialogs.RuleEditorWindow { DataContext = editor }.Show(this);

        vm.AboutRequested += about =>
            new Views.Dialogs.AboutWindow { DataContext = about }.ShowDialog(this);

        vm.ShortcutSheetRequested += sheet =>
            new Views.Dialogs.ShortcutSheetWindow { DataContext = sheet }.Show(this);

        // The menu is generated from data, so these arrive as events rather than as
        // Click handlers on markup that no longer exists.
        // The destructive confirm (2i-6) needs a parent window, which only exists here.
        // Without it every destructive command refuses to run — the safe failure.
        vm.Confirm = Views.Dialogs.DestructiveConfirmWindow.For(this);

        // The warning fixes' reveal half: the hub selects, the view scrolls — the
        // same split OnWarningTapped already uses for a double-clicked warning row.
        vm.WarningRevealRequested += row =>
        {
            var list = vm.ActiveRows.Contains(row) ? _activeList : _inactiveList;
            list.SelectedItem = row;
            list.ScrollIntoView(row);
        };

        // The Edit/View menu's view-halves (UI audit): selection and focus belong to
        // the controls, so the hub raises and the view answers — the ⌘3 shape.
        vm.SelectAllRequested += () => _activeList.SelectAll();

        // Wide layouts only — the segmented case is handled in the hub by switching
        // the segment. No selection side effect (owner's call): focus must not
        // change what is selected.
        vm.InactivePaneFocusRequested += () => _inactiveList.Focus();

        // A keyboard nudge re-selects what it moved: MoveRows rebuilds positions and
        // the ListBox drops the selection, which made repeating Alt+arrows useless.
        vm.ActiveReselectRequested += rows =>
        {
            if (_activeList.SelectedItems is not { } selected) return;
            selected.Clear();
            foreach (var row in rows) selected.Add(row);
        };
        vm.NoteFocusRequested += () =>
        {
            var notes = this.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => AutomationProperties.GetName(t) == "Notes for this mod");
            notes?.Focus();
        };

        vm.SettingsRequested += async page => await ShowSettings(vm, page);
        vm.QuitRequested += Close;

        vm.FirstRunRequested += async () => await ShowFirstRun(vm);

        await vm.InitializeAsync();
        if (vm.NeedsFirstRun) await ShowFirstRun(vm);
    }

    /// <summary>
    /// The 2j wizard. Modal to the main window and shown before it is useful, because
    /// until an instance exists there is nothing behind it to interact with.
    /// </summary>
    private async Task ShowFirstRun(MainWindowViewModel vm)
    {
        var wizard = vm.BuildFirstRun();

        // Step 3 states counts, so it reads the install at the moment it is opened
        // rather than describing paths that were only just confirmed.
        wizard.ImportRequested += () => vm.MeasureFirstRunImport(wizard);

        await new Views.FirstRun.FirstRunWindow { DataContext = wizard }.ShowDialog(this);
    }

    // --- Activity: Follow (2f) ---------------------------------------------
    // "A Follow toggle that auto-scrolls and turns itself off the moment the user
    // scrolls up." The second half is the important half: a log that yanks you back
    // to the bottom while you are reading is worse than one that never follows.
    private ScrollViewer? _activityScroller;
    private bool _autoScrollingActivity;

    private void WireActivityFollow(MainWindowViewModel vm)
    {
        _activityScroller ??= this.FindControl<ScrollViewer>("ActivityScroller");
        if (_activityScroller is null) return;

        vm.VisibleActivityLines.CollectionChanged += (_, _) =>
        {
            if (vm.ActivityFollow) Dispatcher.UIThread.Post(ScrollActivityToEnd, DispatcherPriority.Background);
        };

        _activityScroller.ScrollChanged += (_, args) =>
        {
            // Whether the newest line is on screen. Recomputed on EVERY scroll event,
            // including the ones raised by lines arriving rather than by scrolling,
            // which is exactly when the answer changes without the user doing anything.
            vm.ActivityAtEnd = IsActivityAtEnd();

            // Only a scroll the USER caused disarms Follow. Our own ScrollToEnd
            // raises this event too, and would otherwise switch itself off.
            if (_autoScrollingActivity || args.OffsetDelta.Y >= 0) return;
            vm.ActivityFollow = false;
        };
    }

    /// <summary>
    /// Within a line's height of the bottom. A tolerance rather than an equality: the
    /// extent grows by a fractional pixel as lines wrap, and an exact test would leave
    /// "jump to newest" offered while the newest line is already on screen.
    /// </summary>
    private bool IsActivityAtEnd() =>
        _activityScroller is not { } s
        || s.Offset.Y >= s.Extent.Height - s.Viewport.Height - 20;

    /// <summary>
    /// Re-arms Follow and goes back to the newest line. Both, not just the scroll: the
    /// user asking to see the newest line is asking to keep seeing it.
    /// </summary>
    private void OnActivityJumpToNewest(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        vm.ActivityFollow = true;
        ScrollActivityToEnd();
        vm.ActivityAtEnd = true;
    }

    private void ScrollActivityToEnd()
    {
        if (_activityScroller is null) return;

        _autoScrollingActivity = true;
        _activityScroller.ScrollToEnd();
        // Cleared after the scroll has been laid out, so the ScrollChanged it raises
        // is still seen as ours rather than as the user reaching for the wheel.
        Dispatcher.UIThread.Post(() => _autoScrollingActivity = false, DispatcherPriority.Background);
    }


    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // 2k: the drawer "dismisses on esc". It used to sit behind the command
            // palette's own Escape branch; with the palette gone (O10) it is first.
            if (e.Key == Key.Escape && vm.ShowInfoDrawer)
            {
                vm.CloseInfoDrawerCommand.Execute(null);
                e.Handled = true;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>File → Exit.</summary>
    /// <summary>Opens Settings and reloads, since paths may have moved under us.</summary>
    private async Task ShowSettings(MainWindowViewModel vm, SettingsPage? page = null)
    {
        if (vm.BuildSettings() is not { } settings) return;

        // Set BEFORE the window is shown, so the rail opens on the asked-for page rather
        // than opening on Paths and jumping.
        if (page is { } target) settings.PageIndex = (int)target;

        var window = new SettingsWindow { DataContext = settings };

        // Owned by the Settings window, not the main one: a confirm parented to a window
        // behind this modal would be unreachable behind it.
        settings.Confirm = Views.Dialogs.DestructiveConfirmWindow.For(window);

        // The HUB's confirms get the same treatment while Settings is open (UI audit):
        // the danger-zone Delete/Reset route to hub commands, and their confirm was
        // parented to THIS window — behind the modal, exactly the failure the comment
        // above describes. Swapped for the dialog's lifetime, restored after.
        var previousConfirm = vm.Confirm;
        vm.Confirm = settings.Confirm;

        try
        {
            await window.ShowDialog(this);
        }
        finally
        {
            vm.Confirm = previousConfirm;
        }

        // Drain first. Paths save as they are edited through a SerialWriter, so the last
        // keystroke may still be in flight — and the reload below re-reads the install
        // from disk, which would hand that edit straight back if it landed after.
        await settings.FlushPathsAsync();
        await vm.ReloadPathsAsync();
    }

    private void Wire(ListBox list)
    {
        DragDrop.SetAllowDrop(list, true);
        list.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        list.AddHandler(PointerPressedEvent, OnPointerPressedAfterList, RoutingStrategies.Bubble);
        list.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);
        list.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
        list.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        list.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        list.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private string IndicatorPrefix(ListBox list) =>
        list.Name == "ActiveList" ? "ActiveDropLine" : "InactiveDropLine";

    private Grid? IndicatorFor(ListBox list) => this.FindControl<Grid>(IndicatorPrefix(list));

    private void HideDropLines()
    {
        foreach (var name in new[] { "ActiveDropLine", "InactiveDropLine" })
        {
            if (this.FindControl<Grid>(name) is { } g) g.IsVisible = false;
        }
    }

    /// <summary>
    /// Positions the insertion indicator and colours it for the drop's validity.
    /// The line is shown either way: 3a is explicit that on an invalid drop "the
    /// line still renders — the user must see WHERE they aimed and WHY it failed,
    /// never nothing at all".
    /// </summary>
    private void ShowIndicator(ListBox list, int index, double y, string? invalidReason)
    {
        var prefix = IndicatorPrefix(list);
        if (this.FindControl<Grid>(prefix) is not { } root) return;

        var brushKey = invalidReason is null ? "RmAccentBrush" : "RmDangerBrush";

        // TryFindResource with the ACTIVE THEME VARIANT. The plain lookup misses
        // tokens that live in a ThemeDictionary, and assigning the null it returns
        // wipes the brushes the XAML already set — which renders the indicator as
        // bare text with no line, dot or pill.
        if (this.TryFindResource(brushKey, ActualThemeVariant, out var found) && found is IBrush brush)
        {
            if (this.FindControl<Ellipse>(prefix + "Dot") is { } dot) dot.Fill = brush;
            if (this.FindControl<Border>(prefix + "Line") is { } line) line.Background = brush;
            if (this.FindControl<Border>(prefix + "Pill") is { } pill) pill.Background = brush;
        }

        if (this.FindControl<TextBlock>(prefix + "Text") is { } text)
            text.Text = invalidReason ?? $"→ #{index + 1}";

        // Offset by half the band so the LINE lands on the row boundary.
        root.Margin = new Thickness(0, y - 9, 0, 0);
        root.IsVisible = true;
    }

    /// <summary>
    /// Why a drop here is refused, or null when it is fine. The rule itself lives in
    /// <see cref="ActiveListOps.InvalidDropReason"/>, where it can be tested — a drop
    /// rule that refuses everything and one that refuses nothing look identical until
    /// somebody drags. The reason travels with the pill and to the status bar, because
    /// 3a puts the explanation where the user is already looking.
    /// </summary>
    private static string? InvalidDropReason(ListBox list, int index, RowViewModel? dragged) =>
        list.Name != "ActiveList"
            ? null
            : ActiveListOps.InvalidDropReason([.. list.Items.OfType<RowViewModel>()], index, dragged);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;

        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is not RowViewModel row) return;

        _pressArgs = e;
        _pressRow = row;
        _pressList = list;
        _pressPane = list.Tag as string ?? string.Empty;
        _pressPosition = e.GetPosition(list);
        _dragging = false;
        _pressCollapsesSelection = false;

        // Pressing an already-selected row in a multi-selection must not collapse it:
        // that is the press that starts a multi-row drag, and SelectionMode="Multiple"
        // replaces the selection with the clicked row.
        //
        // Marking the event Handled here does NOT stop it — SelectingItemsControl
        // updates the selection from a class handler, and class handlers run whether
        // or not an instance handler has flagged the event. So instead: snapshot the
        // selection now (this tunnel handler runs before the ListBox sees the press)
        // and put it back in the bubble handler, which runs after.
        var modified = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                       || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var selected = list.SelectedItems?.OfType<RowViewModel>().ToList() ?? [];

        if (!modified && selected.Count > 1 && selected.Contains(row))
        {
            _pressCollapsesSelection = true;
            _selectionAtPress = selected;
        }
        else
        {
            _selectionAtPress = [];
        }
    }

    /// <summary>
    /// Runs after the ListBox has processed the press. Restores a multi-selection the
    /// ListBox just collapsed, so the drag that is about to start carries all of it.
    /// </summary>
    private void OnPointerPressedAfterList(object? sender, PointerPressedEventArgs e)
    {
        if (!_pressCollapsesSelection || sender is not ListBox list) return;

        // Posted rather than applied inline: restoring during the press only wins if
        // the ListBox has finished with the event, and whether it has is exactly the
        // thing two previous attempts got wrong. A background-priority post lands
        // after the whole input pass, whatever that pass turns out to contain.
        var restore = _selectionAtPress;
        Dispatcher.UIThread.Post(() =>
        {
            if (list.SelectedItems is not { } target) return;
            if (target.Count == restore.Count) return;

            target.Clear();
            foreach (var row in restore) target.Add(row);
        }, DispatcherPriority.Background);
    }

    /// <summary>Set when the press landed on an already-selected row of a multi-selection.</summary>
    private bool _pressCollapsesSelection;

    /// <summary>The selection as it was before the ListBox processed the press.</summary>
    private IReadOnlyList<RowViewModel> _selectionAtPress = [];

    /// <summary>Everything in flight for this drag — what the ghost draws and the drop moves.</summary>
    private IReadOnlyList<RowViewModel> _dragRows = [];

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressRow is null || _dragging || _pressArgs is null || _pressList is null) return;
        if (!e.GetCurrentPoint(_pressList).Properties.IsLeftButtonPressed)
        {
            ClearPress();
            return;
        }

        var pos = e.GetPosition(_pressList);
        if (System.Math.Abs(pos.X - _pressPosition.X) < DragThreshold
            && System.Math.Abs(pos.Y - _pressPosition.Y) < DragThreshold)
        {
            return;
        }

        _dragging = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(RowFormat, _pressRow));

        // Source rows dim to 35% and KEEP THEIR PLACE (3a §1). Removing them mid-drag
        // reflows the list under the pointer, which is the single most disorienting
        // thing a reorder can do.
        _pressRow.IsDragSource = true;
        _dragRows = DraggedRows(_pressList, _pressRow);

        // Every row in flight dims, not just the one under the pointer (3a §1).
        foreach (var dragged in _dragRows) dragged.IsDragSource = true;
        ShowGhost(_dragRows);

        if (DataContext is MainWindowViewModel dragVm)
            dragVm.LogDragStarted(_dragRows.Count, _selectionAtPress.Count);
        MoveGhost(e.GetPosition(_ghostLayer ?? (Visual)this));

        try
        {
            await DragDrop.DoDragDropAsync(_pressArgs, data, DragDropEffects.Move);
        }
        finally
        {
            foreach (var dragged in _dragRows) dragged.IsDragSource = false;
            _dragRows = [];
            _pressRow.IsDragSource = false;
            HideGhost();
            StopAutoScroll();
            HideDropLines();
            ClearPress();
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A press we suppressed to protect a multi-selection, released without a drag,
        // is an ordinary click: collapse to the row that was clicked, as the ListBox
        // would have done.
        if (!_dragging && _pressCollapsesSelection && _pressList is { } list && _pressRow is { } row)
            list.SelectedItem = row;

        if (!_dragging) ClearPress();
    }

    private void ClearPress()
    {
        _pressArgs = null;
        _pressRow = null;
        _pressList = null;
        _pressPane = null;
        _dragging = false;
        _pressCollapsesSelection = false;
        _selectionAtPress = [];
    }

    // --- the drag ghost (3a §1) ---------------------------------------------
    // "94% opacity, -0.7 degrees, at most 3 rows, plus a count badge." The rotation
    // is what stops it reading as a row that has somehow escaped its list, and the
    // 94% is what keeps the insertion line legible underneath it.
    private const double GhostOpacityRotation = -0.7;
    private const int GhostMaxRows = 3;

    /// <summary>Offset from the pointer, so the ghost never sits under the cursor.</summary>
    private static readonly Point GhostOffset = new(14, 10);

    private Canvas? _ghostLayer;
    private Border? _ghost;

    private void ShowGhost(IReadOnlyList<RowViewModel> rows)
    {
        _ghostLayer ??= this.FindControl<Canvas>("DragGhostLayer");
        if (_ghostLayer is null || rows.Count == 0) return;

        var stack = new StackPanel { Spacing = 1 };
        foreach (var row in rows.Take(GhostMaxRows))
        {
            stack.Children.Add(new TextBlock
            {
                Text = row is ModRowViewModel mod ? mod.Name : "Separator",
                Classes = { "pri" },
                MaxWidth = 240,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        // The badge counts what is NOT drawn, so three rows and a "+2" add up to five.
        if (rows.Count > GhostMaxRows)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"+{rows.Count - GhostMaxRows} more",
                Classes = { "mono", "ter" },
            });
        }

        _ghost = new Border
        {
            Classes = { "dragGhost" },
            Child = stack,
            RenderTransform = new RotateTransform(GhostOpacityRotation),
        };

        _ghostLayer.Children.Add(_ghost);
    }

    private void MoveGhost(Point atLayer)
    {
        if (_ghost is null) return;

        Canvas.SetLeft(_ghost, atLayer.X + GhostOffset.X);
        Canvas.SetTop(_ghost, atLayer.Y + GhostOffset.Y);
    }

    private void HideGhost()
    {
        if (_ghost is not null) _ghostLayer?.Children.Remove(_ghost);
        _ghost = null;
    }

    /// <summary>
    /// What the ghost shows and the drop moves.
    /// <para>
    /// Deliberately the snapshot taken in the tunnel handler, NOT the ListBox's
    /// current selection. Whether the ListBox has collapsed its selection by now is a
    /// question about Avalonia's internals; what the user had selected when they
    /// pressed is a fact we recorded ourselves. Two attempts to keep the ListBox's
    /// selection intact failed on that distinction.
    /// </para>
    /// </summary>
    private List<RowViewModel> DraggedRows(ListBox list, RowViewModel pressed)
    {
        if (_selectionAtPress.Count > 1 && _selectionAtPress.Contains(pressed))
            return [.. _selectionAtPress];

        var selected = list.SelectedItems?.OfType<RowViewModel>().ToList() ?? [];
        return selected.Contains(pressed) && selected.Count > 1 ? selected : [pressed];
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool carries = e.DataTransfer.Contains(RowFormat);

        // The ghost follows from here rather than from PointerMoved: a drag runs its
        // own loop, and ordinary pointer events stop arriving for its duration.
        if (carries) MoveGhost(e.GetPosition(_ghostLayer ?? (Visual)this));

        if (carries && sender is ListBox list)
        {
            var pointer = e.GetPosition(list);
            UpdateAutoScroll(list, pointer);

            int index = GetDropIndex(list, pointer);
            var reason = InvalidDropReason(list, index, _pressRow);

            e.DragEffects = reason is null ? DragDropEffects.Move : DragDropEffects.None;
            ShowIndicator(list, index, GetInsertLineY(list, index), reason);

            // The reason goes to the status bar too, not only a pill the user has to
            // hunt for (3a §2).
            if (reason is not null && DataContext is MainWindowViewModel vm)
                vm.StatusText = $"Cannot drop here — {reason}";
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is ListBox list && IndicatorFor(list) is { } indicator) indicator.IsVisible = false;
        StopAutoScroll();
    }

    /// <summary>
    /// Scrolls while the pointer sits within 24px of a list's top or bottom edge,
    /// accelerating toward ~600px/s at the very edge (3a). Without it a 200-row list
    /// cannot be reordered across more than a screenful in one gesture.
    /// </summary>
    private void UpdateAutoScroll(ListBox list, Point pointer)
    {
        var viewer = list.FindDescendantOfType<ScrollViewer>();
        if (viewer is null || list.Bounds.Height <= 0)
        {
            StopAutoScroll();
            return;
        }

        // Ramp from 0 at the margin's inner edge to full speed at the very edge, so
        // a pointer resting just inside the zone creeps rather than bolting.
        double direction = 0;
        if (pointer.Y < AutoScrollMargin)
            direction = -(AutoScrollMargin - pointer.Y) / AutoScrollMargin;
        else if (pointer.Y > list.Bounds.Height - AutoScrollMargin)
            direction = (pointer.Y - (list.Bounds.Height - AutoScrollMargin)) / AutoScrollMargin;

        if (direction == 0)
        {
            StopAutoScroll();
            return;
        }

        _autoScrollViewer = viewer;
        _autoScrollDirection = Math.Clamp(direction, -1, 1);

        if (_autoScroll is not null) return;

        _autoScroll = new DispatcherTimer(
            TimeSpan.FromSeconds(AutoScrollTickSeconds), DispatcherPriority.Normal, OnAutoScrollTick);
        _autoScroll.Start();
    }

    private void OnAutoScrollTick(object? sender, EventArgs e)
    {
        if (_autoScrollViewer is not { } viewer) { StopAutoScroll(); return; }

        var delta = _autoScrollDirection * AutoScrollPixelsPerSecond * AutoScrollTickSeconds;
        var y = Math.Clamp(viewer.Offset.Y + delta, 0, Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height));
        viewer.Offset = viewer.Offset.WithY(y);
    }

    private void StopAutoScroll()
    {
        _autoScroll?.Stop();
        _autoScroll = null;
        _autoScrollViewer = null;
        _autoScrollDirection = 0;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        StopAutoScroll();
        HideDropLines();
        if (sender is not ListBox list) return;
        if (e.DataTransfer.TryGetValue(RowFormat) is not { } row) return;

        var sourcePane = _pressPane ?? string.Empty;
        var targetPane = list.Tag as string ?? string.Empty;
        int index = GetDropIndex(list, e.GetPosition(list));

        if (InvalidDropReason(list, index, row) is not null)
        {
            e.Handled = true;
            return;
        }

        // The ghost showed the whole selection; the drop has to move the whole
        // selection, or the app promised something it did not do.
        if (DataContext is MainWindowViewModel vm)
        {
            var rows = _dragRows is { Count: > 1 } many ? many : [row];
            vm.MoveRows(rows, sourcePane, targetPane, index);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Computes the insertion index for a drop, virtualization-aware: only realized
    /// (visible) containers are inspected, and we insert before the first item whose
    /// vertical midpoint is below the pointer (else at the end).
    /// </summary>
    private static int GetDropIndex(ListBox list, Point pointer) =>
        DropTarget.For(RealizedRows(list), pointer.Y, list.ItemCount);

    /// <summary>
    /// The realized containers as the drop arithmetic needs them, with hidden rows
    /// marked rather than measured.
    /// <para>
    /// <c>IsVisible</c> is what excludes them, NOT a zero height — Avalonia never
    /// re-arranges a hidden container, so its <c>Bounds</c> is its pre-filter rectangle
    /// or <c>0,0,0,0</c>, and reading either as geometry is how a filtered drop used to
    /// land somewhere the indicator never pointed. See <see cref="DropTarget"/>.
    /// </para>
    /// </summary>
    private static List<DropRow> RealizedRows(ListBox list)
    {
        var rows = new List<DropRow>();
        foreach (var container in list.GetRealizedContainers())
        {
            if (container is not Control c) continue;

            var index = list.IndexFromContainer(c);
            if (index < 0) continue;

            var top = c.TranslatePoint(new Point(0, 0), list) ?? default;
            rows.Add(new DropRow(index, top.Y, c.Bounds.Height, c.IsVisible));
        }

        return rows;
    }

    /// <summary>The Y (in list coordinates) where the insertion line should sit for a given index.</summary>
    private static double GetInsertLineY(ListBox list, int index)
    {
        Control? atIndex = null;
        Control? last = null;
        int lastIdx = -1;

        foreach (var container in list.GetRealizedContainers())
        {
            if (container is not Control c) continue;
            int i = list.IndexFromContainer(c);
            if (i == index) atIndex = c;
            if (i > lastIdx) { lastIdx = i; last = c; }
        }

        if (atIndex is not null)
            return (atIndex.TranslatePoint(new Point(0, 0), list) ?? default).Y;

        if (last is not null)
        {
            var top = (last.TranslatePoint(new Point(0, 0), list) ?? default).Y;
            return top + last.Bounds.Height;
        }

        return 0;
    }
}
