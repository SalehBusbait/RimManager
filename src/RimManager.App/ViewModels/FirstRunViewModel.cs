using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>What the wizard measured about the install it is about to import.</summary>
public sealed record FirstRunImport(
    int ActiveCount,
    int InactiveCount,
    string SourcesLine,
    int SkippedFolders,
    ImmutableArray<ProposedGroup> Groups);

/// <summary>
/// The four-step first-run wizard (<c>2j</c>): Welcome, Paths, Modlist, Rules.
/// Skippable at any point and re-runnable from Help.
/// <para>
/// Step 3 used to ask for an INSTANCE name — a value that, once instances were
/// removed, was displayed nowhere and affected nothing. It names the first modlist now,
/// which is the thing the toolbar actually shows and the thing the user will switch
/// between. Asking for a name that goes nowhere is worse than not asking; it does not
/// application or asked again about the rules database, and folding both into one
/// window would mean a wizard that shows three steps it has no business showing.
/// </para>
/// <para>
/// Every path check goes through the same <see cref="PathProbe"/> Settings ▸ Paths
/// uses, so "what we found" is worded identically in both places.
/// </para>
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly IFileSystem _fs;
    private readonly Func<FirstRunViewModel, Task> _finish;

    public FirstRunViewModel(
        IFileSystem fs,
        (string? Game, string? Config, string? LocalMods, string? Workshop) detected,
        Func<FirstRunViewModel, Task> finish)
    {
        _fs = fs;
        _finish = finish;

        _gameDir = detected.Game ?? string.Empty;
        _configDir = detected.Config ?? string.Empty;
        _localModsDir = detected.LocalMods ?? string.Empty;
        WorkshopDir = detected.Workshop;

        // "detected" on the Steam card is a claim about this machine, so it is measured
        // rather than assumed: a Workshop folder beside the install is what makes it one.
        IsSteamInstall = detected.Workshop is not null;
        OnStepChanged(FirstRunStep.Welcome);   // seed the chain
        Revalidate();
    }

    /// <summary>The mark for the CURRENT theme (T4): first run opens after the hub
    /// applied the (default or re-run) theme, so a construction-time read is fresh.
    /// Null under headless tests.</summary>
    public Avalonia.Media.Imaging.Bitmap? Mark { get; } = Themes.ThemeAssets.CurrentMark();

    // --- the step chain -------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsPaths), nameof(IsModlist),
        nameof(IsRules), nameof(ShowsHeader), nameof(FooterHint), nameof(PrimaryLabel),
        nameof(CanContinue), nameof(HeaderTitle), nameof(HeaderSubhead))]
    private FirstRunStep _step = FirstRunStep.Welcome;

    public bool IsWelcome => Step == FirstRunStep.Welcome;
    public bool IsPaths => Step == FirstRunStep.Paths;
    public bool IsModlist => Step == FirstRunStep.Modlist;
    public bool IsRules => Step == FirstRunStep.Rules;

    /// <summary>
    /// Step 1 is a centred hero with no header band and no chain — it is the one screen
    /// that is not yet asking for anything, and `2j` draws it that way.
    /// </summary>
    public bool ShowsHeader => !IsWelcome;

    public string HeaderTitle => Step switch
    {
        FirstRunStep.Paths => "Where is RimWorld?",
        FirstRunStep.Modlist => "Your first modlist",
        _ => "Sorting rules",
    };

    public string HeaderSubhead => Step switch
    {
        FirstRunStep.Paths => "RimManager needs three folders. We found them — confirm or change them.",
        FirstRunStep.Modlist => "We imported what the game is running right now. Name that list and you are done.",
        _ => "Optional. The community database is what makes automatic sorting good.",
    };

    public string FooterHint => FirstRunPresenter.FooterHint(Step);
    public string PrimaryLabel => FirstRunPresenter.PrimaryLabel(Step);

    /// <summary>The four progress nodes, restated whenever the step moves.</summary>
    public ImmutableArray<ChainNodeViewModel> Chain { get; } =
    [
        .. FirstRunPresenter.StepTitles.Select((title, i) =>
            new ChainNodeViewModel(i + 1, title, isLast: i == FirstRunPresenter.StepTitles.Length - 1)),
    ];

    partial void OnStepChanged(FirstRunStep value)
    {
        for (var i = 0; i < Chain.Length; i++) Chain[i].State = FirstRunPresenter.NodeState(i, value);
    }

    // --- step 2: paths --------------------------------------------------------

    [ObservableProperty] private bool _isSteamInstall;

    public bool IsOtherInstall
    {
        get => !IsSteamInstall;
        set { if (value) IsSteamInstall = false; }
    }

    partial void OnIsSteamInstallChanged(bool value) => OnPropertyChanged(nameof(IsOtherInstall));

    [ObservableProperty] private string _gameDir = string.Empty;
    [ObservableProperty] private string _configDir = string.Empty;
    [ObservableProperty] private string _localModsDir = string.Empty;

    public string? WorkshopDir { get; }

    partial void OnGameDirChanged(string value)
    {
        // LocalMods is DERIVED, not chosen (UI audit): the wizard used to collect a
        // path FinishFirstRun never read — the domain derives <game>/Mods
        // unconditionally, because that is where RimWorld looks and the only place
        // it looks. The field follows the game folder and says so on screen.
        LocalModsDir = value.Length == 0 ? string.Empty : System.IO.Path.Combine(value, "Mods");
        Revalidate();
    }
    partial void OnConfigDirChanged(string value) => Revalidate();
    partial void OnLocalModsDirChanged(string value) => Revalidate();

    [ObservableProperty] private PathCheck _gameCheck = new(PathVerdict.NotSet, string.Empty);
    [ObservableProperty] private PathCheck _configCheck = new(PathVerdict.NotSet, string.Empty);
    [ObservableProperty] private PathCheck _localModsCheck = new(PathVerdict.NotSet, string.Empty);

    /// <summary>Not a field on this screen — it is the one fact the Steam card states.</summary>
    [ObservableProperty] private PathCheck _workshopCheck = new(PathVerdict.NotSet, string.Empty);

    /// <summary>
    /// `1d`: Continue is disabled until the three required paths validate. A warning is
    /// not a block — an empty-but-present mods folder is a real state to continue from.
    /// </summary>
    public bool PathsValid => !GameCheck.IsMissing && !ConfigCheck.IsMissing && !LocalModsCheck.IsMissing
        && GameDir.Length > 0 && ConfigDir.Length > 0;

    private void Revalidate()
    {
        GameCheck = PathProbe.Game(_fs, GameDir);
        ConfigCheck = PathProbe.Config(_fs, ConfigDir);
        LocalModsCheck = PathProbe.LocalMods(_fs, LocalModsDir);
        WorkshopCheck = PathProbe.Workshop(_fs, WorkshopDir);
        OnPropertyChanged(nameof(PathsValid));
        OnPropertyChanged(nameof(CanContinue));
    }

    // --- step 3: the instance -------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    private string _modlistName = "Default";

    /// <summary>The colour dot shown in the toolbar's modlist selector.</summary>
    [ObservableProperty] private int _paletteIndex = 1;

    /// <summary>On by default: a 214-mod flat list is much harder to reason about.</summary>
    [ObservableProperty] private bool _groupWithSeparators = true;

    /// <summary>
    /// <b>Off</b>, and the screen says why. Design non-negotiable #8 in the place it
    /// matters most: the order the game is running today already works, and sorting it
    /// before the user has anything to compare against throws that away.
    /// </summary>
    [ObservableProperty] private bool _sortImmediately;

    /// <summary>What the import found. Null until the paths have been read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImport), nameof(HasSkipped))]
    private FirstRunImport? _import;

    public bool HasImport => Import is not null;

    /// <summary>
    /// A "0 folders with no About.xml" row is an alarm about a non-problem, drawn in
    /// warning colour beside four facts that are all fine. The row appears only when
    /// something was actually skipped.
    /// </summary>
    public bool HasSkipped => Import is { SkippedFolders: > 0 };

    // --- step 4: the opt-ins --------------------------------------------------

    [ObservableProperty] private bool _downloadCommunityRules = true;
    [ObservableProperty] private bool _checkModUpdatesOnStartup;

    /// <summary>
    /// The count `2j` puts on the mod-update card. Measured, because "queries Steam for
    /// your 324 Workshop items" is a claim about this install.
    /// </summary>
    [ObservableProperty] private int _workshopItemCount;

    /// <summary>
    /// The "You are set up" line. Quotes the real import rather than a template, since
    /// it is the last thing the wizard says before handing over.
    /// </summary>
    public string SetUpSummary => Import is { } import
        ? $"{import.ActiveCount} active mods imported as snapshot #1. Nothing has been written to "
          + "your game folder. Press Ctrl+K at any time to find any command, and Ctrl+/ for the shortcut sheet."
        : "Nothing has been written to your game folder. Press Ctrl+K at any time to find any "
          + "command, and Ctrl+/ for the shortcut sheet.";

    partial void OnImportChanged(FirstRunImport? value) => OnPropertyChanged(nameof(SetUpSummary));

    // --- navigation -----------------------------------------------------------

    public bool CanContinue => Step switch
    {
        FirstRunStep.Paths => PathsValid,
        FirstRunStep.Modlist => ModlistName.Trim().Length > 0,
        _ => true,
    };

    /// <summary>True once the wizard has run to the end or been skipped.</summary>
    public bool Completed { get; private set; }

    /// <summary>Raised when the wizard is finished and the window should close.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when step 3 is entered, so the caller can read the install once.</summary>
    public event Action? ImportRequested;

    [RelayCommand]
    private async Task Next()
    {
        if (!CanContinue) return;

        if (Step == FirstRunStep.Rules)
        {
            await Finish();
            return;
        }

        Step = (FirstRunStep)((int)Step + 1);
        if (Step == FirstRunStep.Modlist && Import is null) ImportRequested?.Invoke();
    }

    [RelayCommand]
    private void Back()
    {
        if (Step == FirstRunStep.Welcome) return;
        Step = (FirstRunStep)((int)Step - 1);
    }

    /// <summary>
    /// Skip still creates the instance from what was detected — the alternative is an
    /// app with no instance at all, which is not a state anything downstream handles.
    /// It skips the questions, not the setup.
    /// </summary>
    [RelayCommand]
    private async Task Skip() => await Finish();

    private async Task Finish()
    {
        Completed = true;
        await _finish(this);
        CloseRequested?.Invoke();
    }
}
