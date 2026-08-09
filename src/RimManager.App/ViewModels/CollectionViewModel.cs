using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.App.ViewModels;

/// <summary>
/// One resolved collection: its members with their four-way reconcile, and the
/// strategy chosen for adding them. Built by the import wizard (<c>2i</c>-3) and
/// adopted by the main view model on commit, which is what makes it a single store —
/// the wizard, the activation and the download batch all read the same object.
/// <para>
/// This used to back the Collection dock tab. That tab is gone (the dock observes
/// standing state; a collection is a one-shot task), so what remains is the model, not
/// a second projection of it: everything the tab's toolbar and footer needed —
/// resolve state, progress text, per-button labels — went with it rather than sitting
/// here unread. The network resolve lives in <c>CollectionService</c>; the arrangement
/// lives in the pure <see cref="CollectionPresenter"/>.
/// </para>
/// </summary>
public sealed partial class CollectionViewModel : ObservableObject
{
    public ObservableCollection<CollectionMemberRowViewModel> Members { get; } = [];

    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _hasResolved;

    /// <summary>The collection's own name, once resolved.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>
    /// How the import joins the load order — <c>2i</c>-3's three radios. Append as a
    /// group is the default because an import that scattered 59 mods through an
    /// existing 200-mod order would be very hard to undo by hand.
    /// </summary>
    private ImportStrategy _strategy = ImportStrategy.AppendGroup;

    public ImportStrategy Strategy
    {
        get => _strategy;
        set
        {
            if (_strategy == value) return;
            _strategy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAppendGroup));
            OnPropertyChanged(nameof(IsMergeAndSort));
            OnPropertyChanged(nameof(IsReplace));
            OnPropertyChanged(nameof(IsNewModlist));
        }
    }

    // Radios. An unchecked notification is ignored: the OTHER radio being checked is
    // what selects a strategy, and acting on false would clear the selection.
    public bool IsAppendGroup
    {
        get => Strategy == ImportStrategy.AppendGroup;
        set { if (value) Strategy = ImportStrategy.AppendGroup; }
    }

    public bool IsMergeAndSort
    {
        get => Strategy == ImportStrategy.MergeAndSort;
        set { if (value) Strategy = ImportStrategy.MergeAndSort; }
    }

    public bool IsReplace
    {
        get => Strategy == ImportStrategy.Replace;
        set { if (value) Strategy = ImportStrategy.Replace; }
    }

    public bool IsNewModlist
    {
        get => Strategy == ImportStrategy.NewModlist;
        set { if (value) Strategy = ImportStrategy.NewModlist; }
    }

    /// <summary>The resolved collection id, used by the "open on Workshop" hand-off.</summary>
    public string? CollectionId { get; private set; }

    // The four-way reconcile from 2i-3's "what RimManager found".
    public int PresentCount { get; private set; }
    public int ToDownloadCount { get; private set; }
    public int UnavailableCount { get; private set; }
    public int AlreadyActiveCount { get; private set; }

    public int SelectedCount => Members.Count(m => m.IsSelected);

    public bool CanDownload => Members.Any(m => m is { IsSelected: true, IsToDownload: true });

    /// <summary>The ids a download would fetch, in collection order.</summary>
    public ImmutableArray<string> SelectedForDownload =>
        [.. Members.Where(m => m is { IsSelected: true, IsToDownload: true }).Select(m => m.PublishedFileId)];

    /// <summary>
    /// Replaces the members from a completed resolve. <paramref name="activeAt"/>
    /// reports a mod's position in the load order, which is what separates "present"
    /// from "already active" — the same install, two very different things to do next.
    /// </summary>
    public void Populate(
        string? collectionId,
        CollectionReport report,
        string title,
        Func<ModId, int?> activeAt)
    {
        ArgumentNullException.ThrowIfNull(report);

        CollectionId = collectionId;
        Title = string.IsNullOrWhiteSpace(title) ? "Imported collection" : title;

        Members.Clear();
        foreach (var row in BuildRows(report, activeAt))
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CollectionMemberRowViewModel.IsSelected)) RefreshSelection();
            };
            Members.Add(row);
        }

        var (present, toDownload, unavailable, alreadyActive) = CollectionPresenter.Reconcile(Members);
        PresentCount = present;
        ToDownloadCount = toDownload;
        UnavailableCount = unavailable;
        AlreadyActiveCount = alreadyActive;
        HasResolved = true;

        OnPropertyChanged(nameof(PresentCount));
        OnPropertyChanged(nameof(ToDownloadCount));
        OnPropertyChanged(nameof(UnavailableCount));
        OnPropertyChanged(nameof(AlreadyActiveCount));
        RefreshSelection();
    }

    /// <summary>The one projection from a resolved report to display rows, ordered and numbered.</summary>
    public static ImmutableArray<CollectionMemberRowViewModel> BuildRows(
        CollectionReport report, Func<ModId, int?> activeAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(activeAt);

        var rows = ImmutableArray.CreateBuilder<CollectionMemberRowViewModel>(report.Members.Length);
        var number = 1;
        foreach (var member in CollectionPresenter.Order(report.Members))
        {
            var position = member.InstalledPackageId is { } id ? activeAt(id) : null;
            rows.Add(new CollectionMemberRowViewModel(member, number++, position));
        }

        return rows.ToImmutable();
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanDownload));
    }
}
