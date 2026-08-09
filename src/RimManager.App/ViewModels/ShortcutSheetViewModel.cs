using System;
using System.Collections.Immutable;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.App.Shortcuts;

namespace RimManager.App.ViewModels;

/// <summary>One row of the ⌘/ sheet: what it does and the keys that do it.</summary>
public sealed record ShortcutSheetRow(string Label, string Gesture);

/// <summary>One block of the sheet — a <see cref="ShortcutGroup"/> and its rows.</summary>
public sealed record ShortcutSheetBlock(string Title, ImmutableArray<ShortcutSheetRow> Rows);

/// <summary>
/// The ⌘/ shortcut sheet (<c>3d</c>), <b>generated from <see cref="ShortcutTable"/></b> and
/// never hand-authored.
/// <para>
/// That is the whole point of it. A hand-written sheet is a second copy of the key
/// bindings, and the copy is wrong the first time a shortcut moves — which is the failure
/// least likely to be noticed, because the sheet is the thing people consult precisely
/// when they do <i>not</i> already know the answer.
/// </para>
/// </summary>
public sealed partial class ShortcutSheetViewModel : ObservableObject
{
    private readonly ImmutableArray<ShortcutSheetBlock> _all;

    public ShortcutSheetViewModel(bool isMac)
    {
        _all =
        [
            .. Enum.GetValues<ShortcutGroup>()
                .Select(group => new ShortcutSheetBlock(
                    Title(group),
                    [
                        .. ShortcutTable.ForSheet(group)
                            .Select(def => new ShortcutSheetRow(
                                def.Label, ShortcutFormatter.Format(def, isMac))),
                    ]))
                // A group with no bound shortcuts renders as an empty heading, which
                // reads as "these exist but we could not find them".
                .Where(block => block.Rows.Length > 0),
        ];

        ModifierNote = isMac
            ? "⌘ is Command. Ctrl on Windows and Linux."
            : "Ctrl shown for Windows and Linux · ⌘ on macOS";

        Refilter();
    }

    /// <summary>
    /// The sheet in two columns (<c>3d</c>): the groups dealt alternately, so the
    /// left-hand column holds the first, third… A single long column is what this was
    /// first, and it made a 28-row sheet taller than most screens.
    /// </summary>
    public ImmutableArray<ShortcutSheetBlock> LeftColumn { get; private set; }

    public ImmutableArray<ShortcutSheetBlock> RightColumn { get; private set; }

    /// <summary>Which modifier the gestures are written with, said once at the foot.</summary>
    public string ModifierNote { get; }

    /// <summary>
    /// Filters by label OR gesture. Both, because the two reasons to open this sheet are
    /// "what is the key for X" and "what does this key I just pressed do".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _filter = string.Empty;

    partial void OnFilterChanged(string value) => Refilter();

    public bool IsEmpty => LeftColumn.Length == 0 && RightColumn.Length == 0;

    public int Count => _all.Sum(b => b.Rows.Length);

    private void Refilter()
    {
        var query = Filter.Trim();

        var matching = string.IsNullOrEmpty(query)
            ? _all
            :
            [
                .. _all
                    .Select(b => b with
                    {
                        Rows =
                        [
                            .. b.Rows.Where(r =>
                                r.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || r.Gesture.Contains(query, StringComparison.OrdinalIgnoreCase)),
                        ],
                    })
                    .Where(b => b.Rows.Length > 0),
            ];

        LeftColumn = [.. matching.Where((_, i) => i % 2 == 0)];
        RightColumn = [.. matching.Where((_, i) => i % 2 == 1)];

        OnPropertyChanged(nameof(LeftColumn));
        OnPropertyChanged(nameof(RightColumn));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Displayed titles for the sheet's blocks, in <c>3d</c>'s order.</summary>
    private static string Title(ShortcutGroup group) => group switch
    {
        ShortcutGroup.LoadOrder => "Load order",
        ShortcutGroup.Edit => "Edit",
        ShortcutGroup.Actions => "Actions",
        ShortcutGroup.Navigate => "Navigate",
        _ => group.ToString(),
    };
}
