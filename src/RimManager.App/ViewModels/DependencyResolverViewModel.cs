using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One card's live state in the resolver — the plan plus whether it has been dealt with.
/// </summary>
public sealed partial class UnmetDependencyViewModel(DependencyCard card) : ObservableObject
{
    public DependencyCard Card { get; } = card;

    public string DisplayName => Card.DisplayName;
    public string PackageIdText => Card.PackageId.Display;
    public string RequiredByText => Card.RequiredByText;
    public string StateText => Card.StateText;
    public string? Unactionable => Card.Unactionable;
    public bool HasUnactionable => Card.Unactionable is not null;

    // !IsIgnored too (UI audit): "Activate all that can be" filtered on this, so a
    // dependency the user explicitly set aside was activated anyway by the footer
    // primary — Ignore has to mean ignored everywhere or it means nothing.
    public bool CanActivate => Card.CanActivate && !IsResolved && !IsIgnored;
    public bool CanDownload => Card.CanDownload && !IsResolved;
    public bool CanOpenWorkshop => Card.CanOpenWorkshop;

    /// <summary>
    /// Dealt with in this sitting. The card stays on screen rather than vanishing: a list
    /// that removes rows as you fix them keeps moving the buttons you are aiming at.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanActivate), nameof(CanDownload), nameof(IsPending))]
    private bool _isResolved;

    /// <summary>What was done, shown in place of the buttons once it is.</summary>
    [ObservableProperty] private string? _outcome;

    /// <summary>Ignored for now — not fixed, and the footer counts it separately.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(CanActivate))]
    private bool _isIgnored;

    public bool IsPending => !IsResolved && !IsIgnored;
}

/// <summary>
/// The missing-dependency resolver (<c>2i</c>-4). Modal — one of only three surfaces that
/// is (#4) — because it activates mods and downloads files.
/// </summary>
public sealed partial class DependencyResolverViewModel : ObservableObject
{
    private readonly Func<ModId, int?> _activate;
    private readonly Func<string, Task<bool>> _download;
    private readonly Action<string> _openWorkshop;

    public DependencyResolverViewModel(
        IEnumerable<DependencyCard> cards,
        Func<ModId, int?> activate,
        Func<string, Task<bool>> download,
        Action<string> openWorkshop)
    {
        _activate = activate;
        _download = download;
        _openWorkshop = openWorkshop;

        foreach (var card in cards) Rows.Add(new UnmetDependencyViewModel(card));
        Summary = DependencyResolver.Summary([.. Rows.Select(r => r.Card)]);
    }

    public ObservableCollection<UnmetDependencyViewModel> Rows { get; } = [];

    /// <summary>"7 unmet · 4 can be activated · 1 can be downloaded · 2 need you".</summary>
    public string Summary { get; }

    /// <summary>
    /// On by default: activating a dependency puts it at the end of the order, which is
    /// almost never where it belongs — a framework that loads after its dependents is
    /// still broken, just differently.
    /// </summary>
    [ObservableProperty] private bool _sortAfterResolving = true;

    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True once anything has been changed, so the caller knows to re-validate.</summary>
    public bool AnythingResolved => Rows.Any(r => r.IsResolved);

    /// <summary>Whether the footer's "Resolve all" has anything left to do.</summary>
    public bool HasResolvable => Rows.Any(r => r.CanActivate);

    [RelayCommand]
    private void Activate(UnmetDependencyViewModel? row)
    {
        if (row is null || !row.CanActivate) return;

        var at = _activate(row.Card.PackageId);
        row.IsResolved = true;

        // Names the position, because "activated" alone leaves you looking for it.
        row.Outcome = at is { } index ? $"Activated at #{index}" : "Activated";
        Refresh();
    }

    [RelayCommand]
    private async Task Download(UnmetDependencyViewModel? row)
    {
        if (row is null || !row.CanDownload || row.Card.WorkshopId is not { } id) return;

        Status = $"Downloading {row.DisplayName}…";
        var ok = await _download(id);

        row.IsResolved = ok;
        row.Outcome = ok ? "Downloaded — rescan to activate it" : null;
        Status = ok
            ? $"Downloaded {row.DisplayName}."
            : $"Could not download {row.DisplayName}. Try the Workshop page.";
        Refresh();
    }

    [RelayCommand]
    private void OpenWorkshop(UnmetDependencyViewModel? row)
    {
        if (row?.Card.WorkshopId is { } id) _openWorkshop(id);
    }

    /// <summary>Not fixed — set aside. Counted apart from resolved so the footer stays honest.</summary>
    [RelayCommand]
    private void Ignore(UnmetDependencyViewModel? row)
    {
        if (row is null) return;
        row.IsIgnored = !row.IsIgnored;
        Refresh();
    }

    /// <summary>
    /// The footer's primary. Activates every dependency that is merely inactive — the only
    /// kind that can be fixed without asking anything further. Downloads are deliberately
    /// NOT included: each is a network call of unknown length, and a button that quietly
    /// starts seven of them is not one anybody can predict.
    /// </summary>
    [RelayCommand]
    private void ResolveAll()
    {
        var done = 0;
        foreach (var row in Rows.Where(r => r.CanActivate).ToList())
        {
            Activate(row);
            done++;
        }

        Status = done == 0
            ? "Nothing here can be activated."
            : $"Activated {done} dependenc{(done == 1 ? "y" : "ies")}.";
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(AnythingResolved));
        OnPropertyChanged(nameof(HasResolvable));
    }
}
