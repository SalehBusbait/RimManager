using System;
using System.ComponentModel;
using System.Windows.Input;
using RimManager.App.ViewModels;

namespace RimManager.App.Tests.Fakes;

/// <summary>
/// Stands in for the live preferences. Records every write, so a test can assert that
/// something was <b>applied</b> rather than merely stored.
/// <para>
/// Shared rather than copied per test file. <see cref="IAppPreferences"/> gains members
/// as phases land, and a second hand-maintained implementation is a fake that drifts —
/// this way a new preference breaks one file and is fixed once.
/// </para>
/// </summary>
public sealed class FakePreferences : IAppPreferences
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private AppTheme _theme = AppTheme.FollowSystem;

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            _theme = value;
            Applied++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));
        }
    }

    /// <summary>How many times the theme actually changed.</summary>
    public int Applied { get; private set; }

    public int FontIndex { get; set; }
    public int UiScalePercent { get; set; } = 100;
    public string UiScaleText => $"{UiScalePercent}%";
    public string LaunchCommand { get; set; } = string.Empty;
    public string LaunchExtraArguments { get; set; } = string.Empty;
    public ICommand ResetLaunchCommand { get; } = new NoCommand();
    public bool ConfirmBeforeApply { get; set; } = true;
    public bool RefuseApplyWithBlockingWarnings { get; set; } = true;
    public int LogLevelIndex { get; set; } = 2;
    public string LogLevelNote => "";
    public string ScanCacheSummary => "";
    public ICommand OpenLogFolder { get; } = new NoCommand();
    public ICommand CopyDiagnostics { get; } = new NoCommand();
    public ICommand ResetLayout { get; } = new NoCommand();
    public ICommand RebuildScanCache { get; } = new NoCommand();
    public ICommand OpenBackupFolder { get; } = new NoCommand();
    public ICommand DeleteAllSnapshots { get; } = new NoCommand();
    public ICommand ResetRimManager { get; } = new NoCommand();
    public ICommand OpenRuleEditor { get; } = new NoCommand();
    public int KeepSnapshots { get; set; } = 100;

    public bool UseTopologicalSort { get; set; } = true;
    public bool UseAlphabeticalSort => !UseTopologicalSort;
    public System.Windows.Input.ICommand ChooseTopologicalSort { get; } =
        new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
    public System.Windows.Input.ICommand ChooseAlphabeticalSort { get; } =
        new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
    public bool SnapshotBeforeSorting { get; set; } = true;
    public bool OpenDockOnCycleBreak { get; set; } = true;
    public bool AutoSortAfterActivate { get; set; }
    public string RulesStatus => "none";
    public ICommand SyncRules { get; } = new NoCommand();
    public bool UseCommunityRules { get; set; } = true;
    public bool UseReplacementsDatabase { get; set; } = true;
    public bool UseKnownGoodDatabase { get; set; } = true;
    public string ReplacementsStatus => "none";
    public string KnownGoodStatus => "none";
    public DatabasePill RulesPill => DatabasePill.For(UseCommunityRules, 0);
    public DatabasePill ReplacementsPill => DatabasePill.For(UseReplacementsDatabase, 0);
    public DatabasePill KnownGoodPill => DatabasePill.For(UseKnownGoodDatabase, 0);
    public string CommunityRulesUrl { get; set; } = string.Empty;
    public string ReplacementsUrl { get; set; } = string.Empty;
    public string KnownGoodBaseUrl { get; set; } = string.Empty;
    public bool ShowGitDirtyOnRows { get; set; } = true;
    public bool FetchReposOnStartup { get; set; }
    public bool CheckModUpdatesOnStartup { get; set; }
    public bool AutoInstallUpdates { get; set; }
    public bool IsComfortableDensity { get; set; }
    public bool ShowTagStripes { get; set; } = true;
    public bool ZebraStriping { get; set; }
    public bool ShowPreviewImages { get; set; } = true;

    private sealed class NoCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => false;
        public void Execute(object? parameter) { }
    }
}
