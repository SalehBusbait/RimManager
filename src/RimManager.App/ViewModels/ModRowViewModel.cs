using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// The single status slot a row can show, in strict precedence order (`1f`).
/// A row NEVER shows two: one 16px slot, one answer, so a column of rows can be
/// scanned vertically without decoding combinations.
/// </summary>
public enum RowStatus
{
    None = 0,

    /// <summary>A dirty git working tree — lowest priority, and purely informational.</summary>
    GitDirty,

    /// <summary>An update is available.</summary>
    UpdateAvailable,

    /// <summary>Something is off but the game will still load.</summary>
    Warning,

    /// <summary>Broken: a missing dependency or an unreadable About.xml. Highest.</summary>
    Broken,
}

/// <summary>A mod row in either pane. <see cref="RowViewModel.Index"/> renumbers after moves.</summary>
public sealed partial class ModRowViewModel : RowViewModel, IModSourceBadge
{
    public Mod Mod { get; }
    public ModId PackageId { get; }
    public string PackageIdText { get; }
    public string Name { get; }

    /// <summary>
    /// The badge's tooltip, which since N1 is the only place the source is written
    /// out — the badge itself is a 9px icon. Never <c>Source.ToString()</c>: the enum
    /// member is spelled <c>Dlc</c>.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The source in one word, for sorting the inactive pane's SRC column.
    /// <para>
    /// Separate from <see cref="Source"/> on purpose: that is the tooltip sentence, and
    /// ordering a column by a sentence describing its values is the kind of thing that
    /// stays correct by luck until a wording change reorders the list.
    /// </para>
    /// </summary>
    public string SourceLabel { get; }

    // The source badge is "glyph AND tint, never tint alone" (1f). The tint is
    // applied by a style class rather than a converter on purpose: a converter
    // would return a resolved brush that goes stale when the theme flips, whereas
    // a class lets the style keep its DynamicResource and re-resolve for free.
    //
    // The same six bools also pick WHICH icon is visible, exactly as the status slot
    // picks among its four. A geometry-valued property on the view model would drag
    // Avalonia resources into it and defeat the point of keeping these testable.
    public bool IsCoreSource => Mod.Source == ModSource.Core;
    public bool IsDlcSource => Mod.Source == ModSource.Dlc;
    public bool IsWorkshopSource => Mod.Source == ModSource.Workshop;
    public bool IsLocalSource => Mod.Source == ModSource.Local;
    public bool IsGitSource => Mod.Source == ModSource.Git;

    /// <summary>Mod version for the VER column, or empty.</summary>
    public string Version { get; }

    /// <summary>
    /// First declared author, for the optional AUTHOR column. First rather than all:
    /// the column is narrow, and a mod's author list is often a credits roll.
    /// </summary>
    public string Author =>
        Mod.Authors.IsDefaultOrEmpty ? string.Empty : Mod.Authors[0];

    /// <summary>
    /// The row's single status. Set by whatever knows: the validator raises Broken
    /// or Warning, the update check raises UpdateAvailable, git raises GitDirty.
    /// The highest wins, which is what keeps the slot unambiguous.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBroken), nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(IsGitDirty), nameof(StatusTip))]
    private RowStatus _status;

    public bool IsBroken => Status == RowStatus.Broken;
    public bool IsWarning => Status == RowStatus.Warning;
    public bool HasUpdate => Status == RowStatus.UpdateAvailable;
    public bool IsGitDirty => Status == RowStatus.GitDirty;

    /// <summary>
    /// Every warning in the dock that names this mod, as <c>Subject</c> or as
    /// <c>Related</c>, already stripped of its leading packageId.
    /// <para>
    /// One rule for both directions, deliberately. N2's done-condition is "every warning
    /// in the dock is reachable from its row, and every row with a warning is reachable
    /// from the chip" — which is only true if a mod named as the OTHER half of an
    /// incompatibility or an order rule carries it too. Two rules (glyph on the subject,
    /// tooltip on either) would be three behaviours to explain and one of them wrong.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTip), nameof(HasWarnings), nameof(WarningHeading))]
    private ImmutableArray<RowWarning> _warnings = [];

    public bool HasWarnings => !Warnings.IsDefaultOrEmpty;

    /// <summary>Mod info's section heading, count included.</summary>
    public string WarningHeading => RowWarnings.SectionHeading(Warnings.IsDefault ? 0 : Warnings.Length);

    /// <summary>
    /// Colour is never the only signal — the glyph is always named in a tooltip, and
    /// since N2 it names the ACTUAL warnings rather than announcing that some exist.
    /// "Has warnings" said precisely what the glyph beside it already said in colour.
    /// </summary>
    public string? StatusTip => Status switch
    {
        _ when IsMissing =>
            "Not installed — this list names it, but it is not in your mod folders",
        RowStatus.Broken or RowStatus.Warning when HasWarnings => RowWarnings.Tip(Warnings),

        // A row can be Broken with no issue naming it: an unreadable About.xml is
        // recorded on the mod itself, never in the validation report.
        RowStatus.Broken => "Broken — a dependency is missing or About.xml could not be read",
        RowStatus.Warning => "Has warnings",
        RowStatus.UpdateAvailable => "An update is available",
        RowStatus.GitDirty => "Uncommitted local changes",
        _ => null,
    };

    /// <summary>
    /// Raises the row's status only if it outranks what is already there, so the
    /// order in which the validator, update check and git scan report does not
    /// change what the row shows.
    /// </summary>
    public void RaiseStatus(RowStatus candidate)
    {
        if (candidate > Status) Status = candidate;
    }

    /// <summary>Core and DLC render at 600 weight — they anchor the list (1f).</summary>
    public bool IsAnchor => Mod.Source is ModSource.Core or ModSource.Dlc;

    // --- conflict badge (N6) -------------------------------------------------
    // A separate slot, NOT part of the status precedence above: a conflict is not a
    // warning — the game loads fine — and a mod can carry both at once. Active rows
    // only: an inactive mod is never loaded, so it overrides nothing and patches
    // nothing, and a badge there would claim a conflict that does not exist.

    /// <summary>Null when this mod contends over nothing that is loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    [NotifyPropertyChangedFor(nameof(IsOverwritingOnly), nameof(IsOverwrittenOnly))]
    [NotifyPropertyChangedFor(nameof(IsMixedConflict))]
    [NotifyPropertyChangedFor(nameof(IsOverrideOnlyConflict), nameof(HasHarmonyConflict))]
    [NotifyPropertyChangedFor(nameof(ConflictTip))]
    private ConflictBadge? _conflicts;

    /// <summary>Any involvement at all — shows the ⚡.</summary>
    public bool HasConflicts => Conflicts is not null;

    // The owner's two-channel system, refined across three screenshots. The MARK
    // beside the bolt carries the override story: + overwrites (green — this row's
    // contested content survives), − overwritten (red — discarded), both as a green +
    // stacked over a red −, each half keeping its singular colour. The BOLT's colour
    // carries the harmony bit: yellow = overrides only, blue = harmony is involved
    // (the mark says whether overrides are too), no bolt = no conflict. Win/lose
    // grammar never touches harmony — every patch runs (§0f).
    public bool IsOverwritingOnly => Conflicts is { IsOverwritingOnly: true };
    public bool IsOverwrittenOnly => Conflicts is { IsOverwrittenOnly: true };
    public bool IsMixedConflict => Conflicts is { IsMixed: true };
    public bool IsOverrideOnlyConflict => Conflicts is { IsOverrideOnly: true };
    public bool HasHarmonyConflict => Conflicts is { HasHarmony: true };

    public string? ConflictTip => Conflicts?.Tip;

    // Selection-relative highlights (N6, MO2's interaction; harmony added in v2):
    // while a mod is selected, red = the selected mod wins against this row, green =
    // this row wins against the selected mod, and the DASHED harmony edge = shares a
    // Harmony target with it — linked, not ranked, no winner named. Set by
    // ApplyConflictRelationsToRows, cleared with the selection.
    [ObservableProperty] private bool _isOverwrittenBySelected;
    [ObservableProperty] private bool _overwritesSelected;
    [ObservableProperty] private bool _sharesHarmonyWithSelected;

    // --- tag pills (v2 §4A.1) ------------------------------------------------
    // EVERY assigned tag is represented — labels while they fit, dots, then "+n";
    // the strip control owns the ladder. Empty renders NOTHING rather than grey,
    // so an untagged list reads as a clean edge.

    [ObservableProperty] private IReadOnlyList<TagPill> _pills = [];

    /// <summary>All of the row's tags, for the pill zone's tooltip.</summary>
    [ObservableProperty] private string? _tagTip;

    /// <summary>
    /// A row for a mod the modlist names but the disk does not have.
    /// <para>
    /// Rendered rather than skipped. A silently shortened load order is how someone
    /// discovers at 2am that the list they were sent does not work — and skipping it also
    /// meant the pane showed fewer mods than the list held, with nothing saying why.
    /// </para>
    /// <para>
    /// Built from the entry's own recorded identity, so the row can name the mod, keep its
    /// place in the order, and hand back its Workshop id to whatever offers to fetch it.
    /// No new control: it is an ordinary row carrying the <see cref="RowStatus.Broken"/>
    /// state that already exists and is already styled and already tooltipped.
    /// </para>
    /// </summary>
    public static ModRowViewModel Missing(ModlistEntry entry) =>
        new(new Mod
        {
            PackageId = ModId.From(entry.Id),
            Name = entry.DisplayName,
            Source = entry.Source ?? ModSource.Workshop,
            RootPath = string.Empty,        // it is not on disk; that is the point
            ModVersion = entry.ModVersion,
            PublishedFileId = entry.PublishedFileId,
        })
        {
            MissingEntry = entry,
            Status = RowStatus.Broken,
        };

    /// <summary>
    /// The list entry this row came from, when the mod is NOT installed.
    /// <para>
    /// Kept because persisting the arrangement rebuilds each entry from the installed mod,
    /// and a missing one has none — so without this a save would rewrite the entry with no
    /// source and no Workshop id, destroying the identity that makes it findable again.
    /// </para>
    /// </summary>
    public ModlistEntry? MissingEntry { get; private init; }

    public bool IsMissing => MissingEntry is not null;

    public ModRowViewModel(Mod mod)
    {
        Mod = mod;
        PackageId = mod.PackageId;
        PackageIdText = mod.PackageId.Display;
        Name = mod.Name;
        Source = ModSourceText.Describe(mod.Source);
        SourceLabel = ModSourceText.Label(mod.Source);
        Version = mod.ModVersion ?? string.Empty;
        _status = mod.HasErrors ? RowStatus.Warning : RowStatus.None;
    }
}
