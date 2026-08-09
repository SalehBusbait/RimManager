using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Workshop;

namespace RimManager.App.ViewModels;

/// <summary>
/// What an imported collection member currently is, locally. <c>2e</c>'s header
/// reconciles four ways, and every row is exactly one of them.
/// </summary>
public enum MemberState
{
    /// <summary>Not installed and available — this is what "Download N" acts on.</summary>
    ToDownload,

    /// <summary>Installed but not in the load order.</summary>
    Present,

    /// <summary>Installed and already in the load order, at a known position.</summary>
    AlreadyActive,

    /// <summary>Deleted or hidden by its author. Nothing can be done about it here.</summary>
    Unavailable,
}

/// <summary>
/// A row in the Collection panel (<c>2e</c>) — a display projection of one
/// <see cref="CollectionMember"/>. Checkboxes belong here (non-negotiable #2): the
/// question genuinely is "which of these do I want".
/// </summary>
public sealed partial class CollectionMemberRowViewModel : ObservableObject
{
    public CollectionMemberRowViewModel(CollectionMember member, int number, int? activeAt)
    {
        ArgumentNullException.ThrowIfNull(member);

        Number = number;
        Name = member.DisplayName;
        PublishedFileId = member.PublishedFileId;
        IsInstalled = member.IsInstalled;
        ActiveAt = activeAt;

        State = member.IsDelisted ? MemberState.Unavailable
              : activeAt is not null ? MemberState.AlreadyActive
              : member.IsInstalled ? MemberState.Present
              : MemberState.ToDownload;

        StatusText = State switch
        {
            MemberState.Unavailable => "unavailable",
            MemberState.AlreadyActive => "active",
            MemberState.Present => "present · inactive",
            _ => "not installed",
        };

        Note = State == MemberState.AlreadyActive ? $"already at #{activeAt}" : string.Empty;

        Action = State switch
        {
            MemberState.Unavailable => "Skip",
            MemberState.AlreadyActive => string.Empty,
            MemberState.Present => "Activate",
            _ => "Download",
        };

        SizeText = UpdatesPresenter.Size(member.SizeBytes);

        // Only what can be acted on starts ticked, and what cannot be acted on cannot
        // be ticked at all — a live checkbox on an unavailable row is a promise the
        // app has no way to keep.
        IsSelected = State == MemberState.ToDownload;
    }

    [ObservableProperty] private bool _isSelected;

    public int Number { get; }
    public string Name { get; }
    public string PublishedFileId { get; }
    public MemberState State { get; }
    public string StatusText { get; }
    public string Note { get; }
    public string Action { get; }
    public string SizeText { get; }
    public bool IsInstalled { get; }
    public int? ActiveAt { get; }

    public bool CanSelect => State is MemberState.ToDownload or MemberState.Present;
    public bool HasNote => Note.Length > 0;
    public bool HasAction => Action.Length > 0;

    public bool IsUnavailable => State == MemberState.Unavailable;
    public bool IsAlreadyActive => State == MemberState.AlreadyActive;
    public bool IsToDownload => State == MemberState.ToDownload;
    public bool IsPresent => State == MemberState.Present;
}
