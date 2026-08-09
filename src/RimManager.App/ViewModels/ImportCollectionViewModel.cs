using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.App.Services;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.App.ViewModels;

/// <summary>
/// The import-collection wizard (<c>2i</c>-3) — <b>modal, two steps</b>, which is what
/// its own heading says it is.
/// <list type="number">
/// <item>Fetch a collection, see what we found, choose how it joins the load order.</item>
/// <item>Review the members and commit.</item>
/// </list>
/// <para>
/// The handoff put step 2 in the Collection dock tab. That tab is gone: the dock is
/// where you <i>observe</i> standing state — every other tab has a count that means
/// something about your install — and a collection is a one-shot task you produce and
/// then leave stale. It is the same argument the design itself makes for why Cycles is
/// a category and not a tab. `2e`'s content moved here whole; nothing was dropped.
/// </para>
/// <para>
/// What deliberately does <b>not</b> move here is the SteamCMD batch. Downloading 342
/// mods is minutes of work, and nothing modal owns background work (`1a`, Busy /
/// loading) — commit closes the wizard and the batch reports to the status bar's
/// activity zone.
/// </para>
/// <para>
/// Everything it needs from outside arrives as a delegate, so it is constructible —
/// and testable — with no window, no network and no scan.
/// </para>
/// </summary>
public sealed partial class ImportCollectionViewModel : ObservableObject
{
    private readonly Func<string, Task<CollectionResolution>> _resolve;
    private readonly Func<ModId, int?> _activeAt;
    private readonly Func<IReadOnlySet<string>, int> _countWouldDeactivate;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string>? _openCollectionPage;

    public ImportCollectionViewModel(
        Func<string, Task<CollectionResolution>> resolve,
        Func<ModId, int?> activeAt,
        Func<IReadOnlySet<string>, int> countWouldDeactivate,
        Func<DateTimeOffset>? now = null,
        Action<string>? openCollectionPage = null,
        bool steamClientRunning = false)
    {
        _resolve = resolve;
        _activeAt = activeAt;
        _countWouldDeactivate = countWouldDeactivate;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _openCollectionPage = openCollectionPage;

        // Measured, not fixed: subscribing is the better outcome when Steam can take
        // the hand-off, and SteamCMD is the only one that works when it cannot.
        _route = steamClientRunning ? ImportRoute.SubscribeInSteam : ImportRoute.SteamCmd;

        // The members, their four-way reconcile and the strategy all live on one model,
        // which the main view model adopts on commit. One projection, one store — the
        // wizard and the thing that carries it out cannot disagree.
        Collection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CollectionViewModel.SelectedCount)
                or nameof(CollectionViewModel.Strategy))
            {
                RefreshCommit();
            }
        };
    }

    /// <summary>The resolved collection: members, counts, strategy.</summary>
    public CollectionViewModel Collection { get; } = new();

    // --- step 1: the URL field ----------------------------------------------

    // Every computed property that reads these is declared here rather than re-raised
    // by hand. Fetch was dead on arrival because OnUrlChanged returned early before
    // raising CanFetch: the VALUE was right, so a test reading the property passed,
    // while the button never re-evaluated IsEnabled and could not be pressed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFetch), nameof(ShowsHint))]
    private string _url = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFetch), nameof(ShowsHint))]
    private bool _isFetching;

    /// <summary>Why the last fetch failed, in the user's words. Empty when it did not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowsHint))]
    private string _errorText = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>
    /// One line under the field, in four states: the reassurance, "fetching…", the
    /// resolved collection, or the failure.
    /// <para>
    /// The fetching state is not cosmetic. Resolving a 476-member collection is two
    /// round trips and takes well over ten seconds — measured, not guessed — and with
    /// only the button greying out it reads as a dead button rather than as work in
    /// progress.
    /// </para>
    /// </summary>
    public bool ShowsHint => !IsFetching && !HasResolved && !HasError;

    /// <summary>
    /// Typing invalidates the previous result: the counts, the member table and the
    /// primary button would otherwise still describe the collection you navigated away
    /// from.
    /// </summary>
    partial void OnUrlChanged(string value)
    {
        if (!HasResolved && !HasError) return;
        HasResolved = false;
        ErrorText = string.Empty;
        Report = null;
        Step = 1;
    }

    public bool CanFetch => !IsFetching && Url.Trim().Length > 0;

    // --- the resolved collection --------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHint))]
    private bool _hasResolved;

    /// <summary>"Anomaly Essentials · 68 items · updated 3 days ago".</summary>
    [ObservableProperty] private string _resolvedLine = string.Empty;

    public CollectionReport? Report { get; private set; }
    public string? CollectionId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public int ItemCount { get; private set; }

    /// <summary>Segment weights for the stacked bar, proportional to the counts.</summary>
    public double InstalledShare { get; private set; }
    public double ToDownloadShare { get; private set; }
    public double UnavailableShare { get; private set; }
    public double AlreadyActiveShare { get; private set; }

    // --- the two steps -------------------------------------------------------

    [ObservableProperty] private int _step = 1;

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        RefreshCommit();
    }

    /// <summary>"Step 1 of 2 · nothing downloads until the next step".</summary>
    public string FooterNote => IsStep1
        ? "Nothing downloads until the next step."
        : "Downloads run in the background — you can keep working.";

    // --- how the missing ones are obtained -----------------------------------

    private ImportRoute _route;

    /// <summary>
    /// Subscribe versus SteamCMD. Offered rather than decided, because the two leave
    /// the install in genuinely different states — Steam-managed and auto-updating, or
    /// an unmanaged copy in <c>Mods/</c>. Falling back from one to the other silently
    /// would hand the user a result they did not choose and cannot easily tell apart.
    /// </summary>
    public ImportRoute Route
    {
        get => _route;
        set
        {
            if (_route == value) return;
            _route = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSubscribeRoute));
            OnPropertyChanged(nameof(IsSteamCmdRoute));
            RefreshCommit();
        }
    }

    public bool IsSubscribeRoute
    {
        get => Route == ImportRoute.SubscribeInSteam;
        set { if (value) Route = ImportRoute.SubscribeInSteam; }
    }

    public bool IsSteamCmdRoute
    {
        get => Route == ImportRoute.SteamCmd;
        set { if (value) Route = ImportRoute.SteamCmd; }
    }

    /// <summary>
    /// The route only governs the missing ones, so with nothing to fetch it governs
    /// nothing and is not shown — a control driving nothing is worse than its absence.
    /// </summary>
    public bool ShowsRoute => Collection.ToDownloadCount > 0;

    /// <summary>A one-line recap on step 2, so the strategy is never a surprise.</summary>
    public string StrategyRecap => Collection.Strategy switch
    {
        ImportStrategy.MergeAndSort => "Merge and sort everything",
        ImportStrategy.Replace => "Replace my load order",
        ImportStrategy.NewModlist => $"Create a new modlist · \"{Title}\"",
        _ => $"Append as a new separator group · \"{Title}\"",
    };

    /// <summary>
    /// The primary names exactly what pressing it will do, because the two acts behind
    /// it cost very different things: adding installed mods is instant, and a SteamCMD
    /// batch is minutes of network.
    /// </summary>
    public string PrimaryLabel
    {
        get
        {
            // "Review 0 items" before anything is fetched is a count of a collection
            // nobody has looked up — a number on screen is a claim, including this one.
            if (IsStep1) return HasResolved ? ImportCollectionPresenter.ReviewLabel(ItemCount) : "Review →";

            var download = Collection.Members.Count(m => m is { IsSelected: true, IsToDownload: true });
            var add = Collection.Members.Count(m => m is { IsSelected: true, IsPresent: true });
            return ImportCollectionPresenter.CommitLabel(
                download, add, Collection.Strategy, Route, ItemCount);
        }
    }

    public bool CanCommit => IsStep1
        ? HasResolved
        : Collection.Members.Any(m => m.IsSelected) || Collection.Strategy == ImportStrategy.Replace;

    private void RefreshCommit()
    {
        OnPropertyChanged(nameof(PrimaryLabel));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(StrategyRecap));
        OnPropertyChanged(nameof(FooterNote));
        OnPropertyChanged(nameof(ShowsRoute));
    }

    partial void OnHasResolvedChanged(bool value) => RefreshCommit();

    [RelayCommand]
    private void Back() => Step = 1;

    /// <summary>
    /// Install path B: hand the whole collection to the running, logged-in Steam
    /// client for its native "Subscribe to all". Kept beside the member table because
    /// that is where you can see how many are missing and decide it is the better
    /// route — subscribing keeps them Steam-managed, SteamCMD does not.
    /// </summary>
    [RelayCommand]
    private void OpenWorkshop()
    {
        if (CollectionId is { } id) _openCollectionPage?.Invoke(id);
    }

    /// <summary>
    /// Step 1's primary. Advances rather than committing: <c>2i</c>-3's own footer
    /// promises nothing happens until the next step, and this is the line that keeps it.
    /// </summary>
    [RelayCommand]
    private void Review()
    {
        if (HasResolved) Step = 2;
    }

    // --- the three strategies ------------------------------------------------

    /// <summary>
    /// What Replace would cost, measured against the real load order rather than
    /// described in the abstract. Recomputed on every resolve, since it depends on
    /// which mods this particular collection names.
    /// </summary>
    [ObservableProperty] private string _replaceConsequence =
        ImportCollectionPresenter.ReplaceConsequence(0);

    // --- the outcome ---------------------------------------------------------

    /// <summary>
    /// True only if the user pressed the primary on step 2. Closing by ✕, Esc or Cancel
    /// leaves it false, so a dismissed wizard can never read as a decision — the same
    /// rule as the destructive confirm (<c>2i</c>-6).
    /// </summary>
    public bool Accepted { get; set; }

    [RelayCommand]
    private async Task Fetch()
    {
        if (!CanFetch) return;

        IsFetching = true;
        ErrorText = string.Empty;
        HasResolved = false;
        Report = null;

        try
        {
            var result = await _resolve(Url.Trim());
            if (!result.Ok)
            {
                ErrorText = result.Error ?? "Could not resolve that collection.";
                return;
            }

            Adopt(result);
        }
        catch (Exception ex)
        {
            ErrorText = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsFetching = false;
        }
    }

    private void Adopt(CollectionResolution result)
    {
        Report = result.Report;
        CollectionId = result.CollectionId;
        Title = string.IsNullOrWhiteSpace(result.Title)
            ? $"Collection {result.CollectionId}"
            : result.Title;
        ItemCount = result.Report.Members.Length;

        Collection.Url = Url.Trim();
        Collection.Populate(result.CollectionId, result.Report, Title, _activeAt);

        var shares = ImportCollectionPresenter.BarShares(
            Collection.PresentCount, Collection.ToDownloadCount,
            Collection.UnavailableCount, Collection.AlreadyActiveCount);
        InstalledShare = shares[0];
        ToDownloadShare = shares[1];
        UnavailableShare = shares[2];
        AlreadyActiveShare = shares[3];

        ResolvedLine = ImportCollectionPresenter.Resolved(
            result.Title, ItemCount, result.UpdatedUtc, _now());

        var memberIds = result.Report.Members
            .Select(m => m.PublishedFileId)
            .ToHashSet(StringComparer.Ordinal);
        ReplaceConsequence = ImportCollectionPresenter.ReplaceConsequence(_countWouldDeactivate(memberIds));

        HasResolved = true;

        // Everything step 2 binds to, announced. Its subtree is built and bound at
        // window load even though IsVisible is false, so a value that is only assigned
        // renders as whatever it was at construction — for Title, blank.
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(InstalledShare));
        OnPropertyChanged(nameof(ToDownloadShare));
        OnPropertyChanged(nameof(UnavailableShare));
        OnPropertyChanged(nameof(AlreadyActiveShare));
        RefreshCommit();
    }
}
