using System.Collections.Generic;
using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Shell;

/// <summary>
/// Builds the menu bar from <see cref="MainWindowViewModel.Menus"/>.
/// <para>
/// Constructed in code rather than through <c>Menu.ItemsSource</c> on purpose.
/// Avalonia's container generation assigns <c>Header = item</c> as a <em>local</em>
/// value, and a local value outranks a <c>ControlTheme</c> setter — so a data-bound
/// menu renders each row's view-model type name however the theme is written.
/// Building real <see cref="MenuItem"/>s also means the <see cref="Menu"/> treats the
/// five top-level entries as top-level (no stray submenu chevron), and separators can
/// be genuine <see cref="Separator"/> controls.
/// </para>
/// <para>
/// The rows still come from one source — <c>MenuModel</c> over <c>ShortcutTable</c> —
/// so the "generated, never hand-authored" property (guide §6) is intact; only the
/// rendering mechanism changed.
/// </para>
/// </summary>
public partial class MenuBarView : UserControl
{
    private readonly Menu? _bar;
    private bool? _collapsed;

    public MenuBarView()
    {
        AvaloniaXamlLoader.Load(this);
        _bar = this.FindControl<Menu>("Bar");
        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    private void Subscribe()
    {
        _collapsed = null;   // a new view model means the bar has to be rebuilt
        if (DataContext is not MainWindowViewModel vm) { Rebuild(); return; }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IsSegmentedLayout)) Rebuild();
        };
        Rebuild();
    }

    /// <summary>
    /// 2k · below 900px "the menu bar collapses to a ☰ button". The five menus become
    /// the children of one item rather than being rebuilt from a second definition, so
    /// there is still exactly one source for what is in the menus.
    /// </summary>
    private void Rebuild()
    {
        if (_bar is null) return;

        if (DataContext is not MainWindowViewModel vm)
        {
            _bar.ItemsSource = null;
            return;
        }

        var collapse = vm.IsSegmentedLayout;
        if (collapse == _collapsed) return;
        _collapsed = collapse;

        var menus = Build(vm.Menus);
        _bar.ItemsSource = collapse
            ? new List<Control> { CollapsedMenuRoot(menus) }
            : menus;

        // The version and counts do not fit beside a 900px toolbar, and the counts are
        // already on the segmented switch down here. The version is the one fact with
        // no other home, so it moves to the tooltip rather than being dropped.
        if (this.FindControl<Control>("VersionAndCounts") is { } strip) strip.IsVisible = !collapse;
    }

    /// <summary>
    /// The single item that IS the menu bar below 900px — File, Edit, View, Tools and
    /// Help all hang off it.
    /// <para>
    /// It was labelled "☰" (U+2630), which non-negotiable #12 forbids and
    /// <c>Icons.axaml</c> says of these characters "DO NOT SHIP THEM": Linux glyph
    /// coverage varies, and on a font without the codepoint the only route to every menu
    /// in the app was an unnamed tofu box. It carried no tooltip and no automation name
    /// either, so nothing else said what it was. Now a real geometry, named and
    /// tooltipped — the same shape the toolbar's ⋯ was converted to.
    /// </para>
    /// </summary>
    private static MenuItem CollapsedMenuRoot(List<Control> menus)
    {
        var item = new MenuItem { ItemsSource = menus };

        if (Application.Current?.FindResource("RmIconMenu") is Geometry geometry)
        {
            item.Header = new PathIcon { Data = geometry, Width = 13, Height = 13 };
        }
        else
        {
            // A resource lookup that misses returns null rather than throwing, and a
            // headerless root would be an invisible menu bar — the worse failure.
            item.Header = "Menus";
        }

        ToolTip.SetTip(item, "File, Edit, View, Tools and Help");
        AutomationProperties.SetName(item, "Menus");
        return item;
    }

    private static List<Control> Build(ImmutableArray<MenuItemViewModel> rows)
    {
        var controls = new List<Control>(rows.Length);

        foreach (var row in rows)
        {
            if (row.Header == MenuItemViewModel.SeparatorHeader)
            {
                controls.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = row.Header,
                Command = row.Command,
                InputGesture = row.Gesture,
            };

            if (row.IsCheckable)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.Bind(MenuItem.IsCheckedProperty, new Binding
                {
                    Source = row,
                    Path = nameof(MenuItemViewModel.IsChecked),
                    Mode = BindingMode.TwoWay,
                });
            }

            if (!row.Items.IsDefaultOrEmpty) item.ItemsSource = Build(row.Items);

            controls.Add(item);
        }

        return controls;
    }
}
