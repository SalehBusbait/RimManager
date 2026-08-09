using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RimManager.App.Views.Dock;

/// <summary>
/// The one dock skeleton, shared by all six tabs.
/// <para>
/// <c>SCREENS.md</c> §"The dock: one skeleton, six tabs" is explicit — "All six tabs
/// are the same shell. Build it once." So this is a templated control rather than six
/// copies of the same DockPanel: 32px tab toolbar on top, a 24px footer at the bottom
/// for the two tabs that have one, and a body split into a master table and a detail
/// panel by a user-draggable, remembered splitter.
/// </para>
/// <para>
/// The 26px tab strip is deliberately <em>not</em> part of this control. The strip is
/// always visible — it is the notification surface — while the shell only exists while
/// the dock is open, so the strip lives one level up in the window.
/// </para>
/// </summary>
public sealed class DockTabShell : TemplatedControl
{
    /// <summary>
    /// Below this the detail panel stops being readable, so the splitter refuses to go
    /// further. The design's widths are 392 (Warnings, Updates) and 452 (Conflicts).
    /// </summary>
    public const double MinimumDetailWidth = 240;

    public static readonly StyledProperty<object?> ToolbarProperty =
        AvaloniaProperty.Register<DockTabShell, object?>(nameof(Toolbar));

    public static readonly StyledProperty<object?> MasterProperty =
        AvaloniaProperty.Register<DockTabShell, object?>(nameof(Master));

    public static readonly StyledProperty<object?> DetailProperty =
        AvaloniaProperty.Register<DockTabShell, object?>(nameof(Detail));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<DockTabShell, object?>(nameof(Footer));

    /// <summary>
    /// An optional third region, right of the detail panel.
    /// <para>
    /// History (<c>2d</c>) is three panes, not two: the master list, the diff, and a
    /// fixed rail carrying the snapshot's metadata and its three actions. The prose in
    /// <c>SCREENS.md</c> reads as though the rail were part of the detail panel, but the
    /// screenshot shows a separate column — and the handoff's own rule is that the
    /// screenshots break ties.
    /// </para>
    /// <para>
    /// It lives on the shell rather than inside History's detail because the shell is
    /// the thing that owns the dock's geometry. A three-column layout nested inside a
    /// two-column one would put History's rail outside the splitter's reach and give
    /// it a second, private set of widths.
    /// </para>
    /// </summary>
    public static readonly StyledProperty<object?> AsideProperty =
        AvaloniaProperty.Register<DockTabShell, object?>(nameof(Aside));

    public static readonly StyledProperty<bool> HasAsideProperty =
        AvaloniaProperty.Register<DockTabShell, bool>(nameof(HasAside));

    /// <summary>
    /// The aside's width. Resizable and remembered, like the detail panel: the rail
    /// carries wrapping text ("yes — written to ModsConfig.xml") whose comfortable
    /// width depends on the theme's font, so a number chosen here cannot be right for
    /// everyone.
    /// </summary>
    public static readonly StyledProperty<double> AsideWidthProperty =
        AvaloniaProperty.Register<DockTabShell, double>(
            nameof(AsideWidth),
            defaultValue: 248,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> AsideHeaderProperty =
        AvaloniaProperty.Register<DockTabShell, string?>(nameof(AsideHeader));

    public static readonly StyledProperty<string?> DetailHeaderProperty =
        AvaloniaProperty.Register<DockTabShell, string?>(nameof(DetailHeader));

    public static readonly StyledProperty<string?> DetailCounterProperty =
        AvaloniaProperty.Register<DockTabShell, string?>(nameof(DetailCounter));

    public static readonly StyledProperty<bool> HasDetailProperty =
        AvaloniaProperty.Register<DockTabShell, bool>(nameof(HasDetail), defaultValue: true);

    /// <summary>24px footer — only Warnings and Collection have one.</summary>
    public static readonly StyledProperty<bool> HasFooterProperty =
        AvaloniaProperty.Register<DockTabShell, bool>(nameof(HasFooter));

    /// <summary>
    /// The detail panel's width in DIPs. Two-way by default because the splitter is
    /// the thing that changes it and the value is persisted per tab.
    /// </summary>
    public static readonly StyledProperty<double> DetailWidthProperty =
        AvaloniaProperty.Register<DockTabShell, double>(
            nameof(DetailWidth),
            defaultValue: 392,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// The detail column's length, bound two-way from the template.
    /// <para>
    /// A <see cref="GridSplitter"/> resizes a <see cref="ColumnDefinition"/>, not a
    /// child's Width, so the shell has to hand the grid a <see cref="GridLength"/> and
    /// read the drag back out of it. Keeping the conversion here rather than in a value
    /// converter is what lets the clamp and the collapse-when-there-is-no-detail rule
    /// live in one testable place.
    /// </para>
    /// </summary>
    public static readonly DirectProperty<DockTabShell, GridLength> DetailColumnProperty =
        AvaloniaProperty.RegisterDirect<DockTabShell, GridLength>(
            nameof(DetailColumn),
            o => o.DetailColumn,
            (o, v) => o.DetailColumn = v,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Below this the rail's buttons start wrapping their labels.</summary>
    public const double MinimumAsideWidth = 180;

    /// <summary>The aside column's length, bound two-way. Same shape as
    /// <see cref="DetailColumnProperty"/>; see its remarks for why.</summary>
    public static readonly DirectProperty<DockTabShell, GridLength> AsideColumnProperty =
        AvaloniaProperty.RegisterDirect<DockTabShell, GridLength>(
            nameof(AsideColumn),
            o => o.AsideColumn,
            (o, v) => o.AsideColumn = v,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// 2k · below 900px the detail panel moves BELOW the master table instead of beside
    /// it. A fixed 392px detail leaves the master about 450px at that width, and a
    /// five-column table is not readable in 450px — the ISSUE column ends up at 76px.
    /// <para>
    /// The design specifies the two breakpoints but says nothing about the dock, so
    /// this is our call: stacked keeps BOTH halves usable, where collapsing the detail
    /// would take away the explanation of the row you just selected.
    /// </para>
    /// </summary>
    public static readonly StyledProperty<bool> IsStackedProperty =
        AvaloniaProperty.Register<DockTabShell, bool>(nameof(IsStacked));

    /// <summary>
    /// How tall the stacked detail panel is. Deliberately NOT the same value as
    /// <see cref="DetailWidth"/>: they are different measurements of different
    /// arrangements, and sharing one would make dragging the splitter in one layout
    /// silently resize the other.
    /// </summary>
    public static readonly StyledProperty<double> DetailStackHeightProperty =
        AvaloniaProperty.Register<DockTabShell, double>(
            nameof(DetailStackHeight),
            defaultValue: 120,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Below this the stacked panel shows a heading and nothing else.</summary>
    public const double MinimumStackHeight = 72;

    /// <summary>
    /// The stacked detail's ROW length, bound two-way from the template — the same
    /// arrangement as <see cref="DetailColumn"/>, and for the same reason: a
    /// <see cref="GridSplitter"/> resizes a <see cref="RowDefinition"/>, never a
    /// child's Height. Giving the host a Height instead left it 120px tall inside a row
    /// the splitter had already grown, so dragging the divider up opened a blank band.
    /// </summary>
    public static readonly DirectProperty<DockTabShell, GridLength> DetailStackRowProperty =
        AvaloniaProperty.RegisterDirect<DockTabShell, GridLength>(
            nameof(DetailStackRow),
            o => o.DetailStackRow,
            (o, v) => o.DetailStackRow = v,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private GridLength _detailColumn = new(392, GridUnitType.Pixel);
    private GridLength _asideColumn = new(0, GridUnitType.Pixel);
    private GridLength _detailStackRow = new(0, GridUnitType.Pixel);
    private bool _syncing;

    private Grid? _columns;
    private Border? _detail;
    private Control? _detailSplitter;
    private Control? _stackSplitter;
    private Border? _stackHost;

    static DockTabShell()
    {
        DetailWidthProperty.Changed.AddClassHandler<DockTabShell>((s, _) => s.SyncColumnFromWidth());
        HasDetailProperty.Changed.AddClassHandler<DockTabShell>((s, _) => s.SyncColumnFromWidth());
        AsideWidthProperty.Changed.AddClassHandler<DockTabShell>((s, _) => s.SyncAsideFromWidth());
        HasAsideProperty.Changed.AddClassHandler<DockTabShell>((s, _) => s.SyncAsideFromWidth());
        IsStackedProperty.Changed.AddClassHandler<DockTabShell>((s, _) => s.ApplyArrangement());
        DetailStackHeightProperty.Changed.AddClassHandler<DockTabShell>((s, _) => s.SyncStackRowFromHeight());
    }

    public GridLength DetailStackRow
    {
        get => _detailStackRow;
        set
        {
            SetAndRaise(DetailStackRowProperty, ref _detailStackRow, value);

            // The splitter wrote a new length: mirror it onto DetailStackHeight, which
            // is the value a caller can persist.
            if (_syncing || !IsStacked || !value.IsAbsolute) return;
            if (value.Value >= MinimumStackHeight) DetailStackHeight = value.Value;
        }
    }

    private void SyncStackRowFromHeight()
    {
        _syncing = true;
        try
        {
            var height = IsStacked && HasDetail
                ? Math.Max(MinimumStackHeight, DetailStackHeight)
                : 0;
            SetAndRaise(DetailStackRowProperty, ref _detailStackRow,
                new GridLength(height, GridUnitType.Pixel));
        }
        finally
        {
            _syncing = false;
        }
    }

    public bool IsStacked
    {
        get => GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    public double DetailStackHeight
    {
        get => GetValue(DetailStackHeightProperty);
        set => SetValue(DetailStackHeightProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _columns = e.NameScope.Find<Grid>("PART_Columns");
        _detail = e.NameScope.Find<Border>("PART_Detail");
        _detailSplitter = e.NameScope.Find<Control>("PART_DetailSplitter");
        _stackSplitter = e.NameScope.Find<Control>("PART_StackSplitter");
        _stackHost = e.NameScope.Find<Border>("PART_StackHost");

        ApplyArrangement();
    }

    /// <summary>
    /// Moves the detail panel between its column and the row below the table. The same
    /// Border travels, so the detail's own scroll position and its content's state
    /// survive the move — the mod-info drawer does exactly this for the same reason.
    /// </summary>
    private void ApplyArrangement()
    {
        if (_columns is null || _detail is null || _stackHost is null) return;

        var stacked = IsStacked && HasDetail;

        if (stacked)
        {
            if (!ReferenceEquals(_detail.Parent, _stackHost))
            {
                _columns.Children.Remove(_detail);
                _stackHost.Child = _detail;
            }

            // The panel's rule moves from its left edge to its top edge: stacked, it
            // divides the body horizontally, not vertically.
            _detail.BorderThickness = new Thickness(0, 1, 0, 0);
        }
        else if (!ReferenceEquals(_detail.Parent, _columns))
        {
            _stackHost.Child = null;
            _detail.BorderThickness = new Thickness(1, 0, 0, 0);
            if (!_columns.Children.Contains(_detail)) _columns.Children.Add(_detail);
        }

        if (_stackHost is not null) _stackHost.IsVisible = stacked;
        if (_stackSplitter is not null) _stackSplitter.IsVisible = stacked;
        if (_detailSplitter is not null) _detailSplitter.IsVisible = HasDetail && !stacked;

        // With the detail below, its column and the splitter beside it give their width
        // back to the table — which is the entire point of stacking. And beside the
        // table, the stacked row has to give its height back for the same reason.
        SyncColumnFromWidth();
        SyncStackRowFromHeight();
    }

    public object? Toolbar
    {
        get => GetValue(ToolbarProperty);
        set => SetValue(ToolbarProperty, value);
    }

    public object? Master
    {
        get => GetValue(MasterProperty);
        set => SetValue(MasterProperty, value);
    }

    public object? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public object? Aside
    {
        get => GetValue(AsideProperty);
        set => SetValue(AsideProperty, value);
    }

    public bool HasAside
    {
        get => GetValue(HasAsideProperty);
        set => SetValue(HasAsideProperty, value);
    }

    public double AsideWidth
    {
        get => GetValue(AsideWidthProperty);
        set => SetValue(AsideWidthProperty, value);
    }

    /// <summary>Caps micro label above the aside, e.g. "SNAPSHOT #48".</summary>
    public string? AsideHeader
    {
        get => GetValue(AsideHeaderProperty);
        set => SetValue(AsideHeaderProperty, value);
    }

    /// <summary>Caps micro label above the detail panel, e.g. "SELECTED WARNING".</summary>
    public string? DetailHeader
    {
        get => GetValue(DetailHeaderProperty);
        set => SetValue(DetailHeaderProperty, value);
    }

    /// <summary>Right-aligned provenance for the detail header, e.g. "1 of 12 · ↑↓".</summary>
    public string? DetailCounter
    {
        get => GetValue(DetailCounterProperty);
        set => SetValue(DetailCounterProperty, value);
    }

    public bool HasDetail
    {
        get => GetValue(HasDetailProperty);
        set => SetValue(HasDetailProperty, value);
    }

    public bool HasFooter
    {
        get => GetValue(HasFooterProperty);
        set => SetValue(HasFooterProperty, value);
    }

    public double DetailWidth
    {
        get => GetValue(DetailWidthProperty);
        set => SetValue(DetailWidthProperty, value);
    }

    public GridLength DetailColumn
    {
        get => _detailColumn;
        set
        {
            SetAndRaise(DetailColumnProperty, ref _detailColumn, value);

            // The splitter wrote a new length: mirror it back onto DetailWidth so the
            // caller can persist it. Guarded because SyncColumnFromWidth writes here.
            if (_syncing || !HasDetail || !value.IsAbsolute) return;
            if (value.Value >= MinimumDetailWidth) DetailWidth = value.Value;
        }
    }

    public GridLength AsideColumn
    {
        get => _asideColumn;
        set
        {
            SetAndRaise(AsideColumnProperty, ref _asideColumn, value);

            if (_syncing || !HasAside || !value.IsAbsolute) return;
            if (value.Value >= MinimumAsideWidth) AsideWidth = value.Value;
        }
    }

    private void SyncColumnFromWidth()
    {
        _syncing = true;
        try
        {
            // Stacked, the detail is not in this grid at all, so its column is zero —
            // otherwise 392px of empty column would sit between the table and nothing.
            var width = HasDetail && !IsStacked ? Math.Max(MinimumDetailWidth, DetailWidth) : 0;
            SetAndRaise(DetailColumnProperty, ref _detailColumn, new GridLength(width, GridUnitType.Pixel));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncAsideFromWidth()
    {
        _syncing = true;
        try
        {
            var width = HasAside ? Math.Max(MinimumAsideWidth, AsideWidth) : 0;
            SetAndRaise(AsideColumnProperty, ref _asideColumn, new GridLength(width, GridUnitType.Pixel));
        }
        finally
        {
            _syncing = false;
        }
    }
}
