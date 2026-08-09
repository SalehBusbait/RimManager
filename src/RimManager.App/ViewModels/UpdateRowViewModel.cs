using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.App.ViewModels;

/// <summary>
/// One row in the Updates table (<c>2b</c>) — a display projection of one
/// <see cref="ModUpdateStatus"/>. This is the <b>only</b> mod table with checkboxes
/// (non-negotiable #2): here the question genuinely is "which of these", where in the
/// mod lists membership is simply which list the row is in.
/// </summary>
public sealed partial class UpdateRowViewModel : ObservableObject, IModSourceBadge
{
    public UpdateRowViewModel(ModUpdateStatus status, DateTimeOffset now, ModSnooze? snooze = null)
    {
        ArgumentNullException.ThrowIfNull(status);

        Id = status.Id;
        Name = status.Name;
        PublishedFileId = status.PublishedFileId;
        Source = status.Source;
        IsUpdate = status.Status == UpdateStatus.UpdateAvailable;
        IsDelisted = status.Status == UpdateStatus.Delisted;
        IsSnoozed = snooze is not null;
        SnoozeNote = snooze is null ? string.Empty : Describe(snooze);

        InstalledVersion = string.IsNullOrWhiteSpace(status.InstalledVersion)
            ? "—" : status.InstalledVersion!;

        IsPreRelease = UpdatesPresenter.LooksPreRelease(status.InstalledVersion);

        // Local edits are a git working-tree fact. Nothing wires git into the update
        // check yet, so this is explicitly false rather than assumed-safe by omission:
        // the safe-set rule reads it, and it must not quietly start returning true.
        HasLocalEdits = false;

        PublishedText = UpdatesPresenter.Published(status.RemoteUtc, now);
        PublishedExact = status.RemoteUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "unknown";
        SizeText = UpdatesPresenter.Size(status.SizeBytes);

        StatusText = status.Status switch
        {
            _ when IsSnoozed => "snoozed",
            UpdateStatus.UpdateAvailable when IsPreRelease => "pre-release",
            UpdateStatus.UpdateAvailable => "update",
            UpdateStatus.Delisted => "delisted",
            UpdateStatus.NotTracked => "not tracked",
            _ => "—",
        };

        // SourceLetter/SourceClass are GONE (UI audit follow-up): the Updates rows
        // were the last surface drawing a LETTER where every other row draws the
        // shared icon badge — one source, two marks. IModSourceBadge below hands the
        // row to the same SourceBadgeTemplate the mod lists use.
    }

    /// <summary>
    /// Ticked for the batch. Only <see cref="UpdatesPresenter.IsSafeToBatch"/> rows are
    /// ever ticked by the header checkbox; the rest need a deliberate click.
    /// </summary>
    [ObservableProperty] private bool _isSelected;

    public ModId Id { get; }
    public string Name { get; }
    public string? PublishedFileId { get; }
    public ModSource Source { get; }

    // --- IModSourceBadge: the shared 9px icon badge, one grammar with the lists.
    public bool IsCoreSource => Source == ModSource.Core;
    public bool IsDlcSource => Source == ModSource.Dlc;
    public bool IsWorkshopSource => Source == ModSource.Workshop;
    public bool IsLocalSource => Source == ModSource.Local;
    public bool IsGitSource => Source == ModSource.Git;

    /// <summary>The badge's tooltip — the 9px icon's only words.</summary>
    string IModSourceBadge.Source => Source.ToString();

    public string InstalledVersion { get; }
    public string PublishedText { get; }
    public string PublishedExact { get; }
    public string SizeText { get; }
    public string StatusText { get; }
    public string SnoozeNote { get; }


    public bool IsUpdate { get; }
    public bool IsDelisted { get; }
    public bool IsSnoozed { get; }
    public bool IsPreRelease { get; }
    public bool HasLocalEdits { get; }

    /// <summary>A checkbox on a row with nothing to update would be a dead control.</summary>
    public bool CanSelect => IsUpdate;

    public bool HasSnoozeNote => SnoozeNote.Length > 0;

    // The state column is toned by what it says: an ordinary update reads accent, a
    // release candidate or a dirty working tree reads warning, a delisted item danger.
    public bool IsStateWarning => IsPreRelease || HasLocalEdits;
    public bool IsStateBad => IsDelisted;
    public bool IsStateAccent => IsUpdate && !IsPreRelease && !IsSnoozed;

    private static string Describe(ModSnooze snooze) => snooze.Kind switch
    {
        SnoozeKind.OneWeek => $"snoozed until {snooze.SnoozedUtc.AddDays(7).ToLocalTime():MMM d}",
        SnoozeKind.UntilNextVersion => $"snoozed past {snooze.AtModVersion ?? "this version"}",
        _ => $"snoozed until after {snooze.AtGameVersion ?? "this game version"}",
    };
}
