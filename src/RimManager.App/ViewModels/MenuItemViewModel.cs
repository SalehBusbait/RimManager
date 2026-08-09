using System.Collections.Immutable;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.App.Shortcuts;

namespace RimManager.App.ViewModels;

/// <summary>
/// One rendered menu row. Built from <see cref="MenuModel"/> + <see cref="ShortcutTable"/>,
/// never hand-authored in XAML, so the label a user reads and the gesture that
/// actually fires come from the same place (guide §6).
/// </summary>
public sealed partial class MenuItemViewModel : ObservableObject
{
    /// <summary>Avalonia's default MenuItem theme renders a "-" header as a rule.</summary>
    public const string SeparatorHeader = "-";

    /// <summary>The label; "-" for a separator.</summary>
    [ObservableProperty] private string _header = string.Empty;

    /// <summary>The gesture shown right-aligned, and bound as the real key binding.</summary>
    public KeyGesture? Gesture { get; init; }

    public ICommand? Command { get; init; }

    /// <summary>Submenu rows; empty for a leaf.</summary>
    public ImmutableArray<MenuItemViewModel> Items { get; init; } = [];

    public bool IsCheckable { get; init; }

    [ObservableProperty] private bool _isChecked;

    /// <summary>
    /// Disabled items keep their shortcut visible (`2h`) — a greyed row with its key
    /// still teaches, which is why an unimplemented command renders rather than hides.
    /// </summary>
    public bool HasCommand => Command is not null;

    public static MenuItemViewModel Separator() => new() { Header = SeparatorHeader };

    /// <summary>
    /// Builds the whole bar. <paramref name="commandFor"/> resolves a shortcut id to
    /// the command that runs it; returning null leaves the row visible but disabled.
    /// </summary>
    public static ImmutableArray<MenuItemViewModel> BuildBar(Func<string, ICommand?> commandFor) =>
    [
        .. MenuModel.Menus.Select(menu => new MenuItemViewModel
        {
            Header = menu.Title,
            Items = Build(menu.Rows, commandFor),
        })
    ];

    private static ImmutableArray<MenuItemViewModel> Build(
        ImmutableArray<MenuRow> rows, Func<string, ICommand?> commandFor) =>
    [
        .. rows.Select(row =>
        {
            if (row.IsSeparator) return Separator();

            return new MenuItemViewModel
            {
                Header = row.DisplayLabel,
                Gesture = row.ShortcutId is { } id ? ShortcutGesture.For(id) : null,
                Command = row.ShortcutId is { } cid ? commandFor(cid) : null,
                IsCheckable = row.IsCheckable,
                Items = Build(row.ChildrenOrEmpty, commandFor),
            };
        })
    ];
}
