using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// N5a's question — <b>when does work run</b> — for the two answers that were wrong.
/// <para>
/// Source checks, because <c>MainWindowViewModel</c> cannot be constructed under test and
/// both rules are about a call site rather than a value. The same shape as the guards on
/// gesture text and rule-source parity: a rule that can only be stated about the code is
/// still worth stating.
/// </para>
/// </summary>
public sealed class WorkScheduleTests
{
    private static string Hub => RepoPaths.HubSource();

    /// <summary>
    /// The automatic update check must not open the dock.
    /// <para>
    /// <c>RevealDock(DockUpdates)</c> used to run on every successful check, and the startup
    /// check is a successful check — so launching the app took over the bottom of the window
    /// with an answer to a question nobody had asked yet. The tab strip's count badge is how
    /// an automatic result announces itself; the pane is what you open when you want to read
    /// it.
    /// </para>
    /// </summary>
    [Fact]
    public void The_automatic_update_check_does_not_reveal_the_dock()
    {
        Hub.Should().Contain("CheckUpdatesAsync(announce: false)",
            "the check that runs inside a reload is not one the user asked for");

        Hub.Should().NotContain("RevealDock(DockUpdates);\n        }",
            "revealing has to sit behind the announce flag, never at the top level of the "
            + "success path");
    }

    /// <summary>
    /// An automatic run must not take the status line either. It runs at the end of a reload,
    /// and the line it would overwrite is the one describing the list the user just opened —
    /// so switching modlists would flash the new list's name and then replace it with a
    /// conflict summary. Background progress belongs in the status bar's activity zone, which
    /// is where <c>1a</c> puts it and where both scans already report.
    /// </summary>
    [Fact]
    public void An_automatic_run_does_not_take_the_status_line()
    {
        Hub.Should().Contain("if (announce) StatusText = \"Checking for updates",
            "the reload's own status line survives an automatic check");

        Hub.Should().Contain("if (announce) StatusText = \"Analyzing conflicts",
            "and an automatic scan");
    }

    /// <summary>
    /// And it runs once per session, not once per reload.
    /// <para>
    /// It sits inside <c>ReloadAsync</c>, which runs on a modlist switch, on F5, on the
    /// mod-changes strip's Rescan and when the install is re-selected after Settings. Updates
    /// are about what is <b>installed</b>, and none of those change that — the switch path's
    /// own comment says so — so the preference meant "a Workshop round-trip every time you
    /// change lists".
    /// </para>
    /// </summary>
    [Fact]
    public void The_automatic_update_check_runs_once_per_session()
    {
        Hub.Should().Contain("CheckModUpdatesOnStartup && !_autoUpdateCheckDone",
            "a reload is not a session");

        // The flag has to be SET where it is tested, or "once" is a comment rather than a
        // behaviour.
        Regex.IsMatch(Hub, @"_autoUpdateCheckDone\s*=\s*true").Should().BeTrue();
    }

    /// <summary>
    /// The user-invoked command still shows the answer. Silencing both would fix the
    /// interruption by removing the feature — pressing "Check for updates" and being shown
    /// nothing is worse than being shown too much.
    /// </summary>
    [Fact]
    public void The_user_invoked_check_still_reveals()
    {
        Hub.Should().Contain("CheckUpdatesAsync(announce: true)");
    }

    /// <summary>
    /// Conflicts follow the same reveal rule, and run on <b>every</b> reload rather than once
    /// a session — startup and modlist switch both. A conflict is a property of the ACTIVE
    /// list, which is exactly what a reload rebuilds, so a cached result from the previous
    /// list would be a badge describing mods that are no longer loaded. That is the opposite
    /// of the update check, which is about what is <em>installed</em>, and the two are next to
    /// each other so the difference is visible.
    /// </summary>
    [Fact]
    public void The_conflict_scan_runs_on_every_reload_and_does_not_reveal()
    {
        Hub.Should().Contain("AnalyzeConflictsAsync(announce: false)");
        Hub.Should().Contain("AnalyzeConflictsAsync(announce: true)");

        Hub.Should().NotContain("_autoConflictScanDone",
            "conflicts are per-list, so a once-per-session guard would be wrong here — "
            + "the absence of one is the decision");
    }

    /// <summary>
    /// A second reload arriving mid-scan must <b>queue</b>, not be dropped.
    /// <para>
    /// Dropping was right while the scan was manual-only — a double-click should not start a
    /// second Cecil pass. It became a bug the moment it ran automatically: switching lists
    /// while the previous list's scan is still going would silently skip the new one, leaving
    /// the tab describing mods that are no longer loaded. Which is the precise failure that
    /// "runs on every reload" exists to prevent, reintroduced one commit later by the guard
    /// that used to be correct.
    /// </para>
    /// </summary>
    [Fact]
    public void A_reload_during_a_scan_queues_another_rather_than_dropping_it()
    {
        Hub.Should().Contain("_conflictScanQueued = true",
            "the request is remembered");

        Hub.Should().Contain("_conflictScanQueued = false",
            "and consumed when the running scan finishes");
    }

    /// <summary>
    /// The conflict scan is <b>awaited</b> under the load state; the update check is not.
    /// <para>
    /// The line is what the answer decorates. A conflict lands on a ROW — N6 puts a ⚡ badge
    /// on each one — so a list rendered before it finishes is a list that changes under the
    /// user a second later. An update lands in a TAB, and a tab filling in is what a badge is
    /// for. It is also the difference between local work and a network call that can hang.
    /// </para>
    /// <para>
    /// The order matters too, and is the whole of the middle ground: the update check is
    /// started <em>before</em> the conflict wait, so the two overlap and the network check
    /// usually finishes inside a phase that is already happening.
    /// </para>
    /// </summary>
    [Fact]
    public void Conflicts_are_waited_for_and_updates_are_not()
    {
        Hub.Should().Contain("await AnalyzeConflictsAsync(announce: false)",
            "the list does not appear until the badges can be right");

        Hub.Should().Contain("_ = CheckUpdatesAsync(announce: false)",
            "and nobody waits on the network");

        Hub.IndexOf("_ = CheckUpdatesAsync(announce: false)", StringComparison.Ordinal)
            .Should().BeLessThan(
                Hub.IndexOf("await AnalyzeConflictsAsync(announce: false)", StringComparison.Ordinal),
                "updates start first so they run inside the conflict phase for free");
    }

    /// <summary>
    /// The wait has no escape hatch, and that is a decision rather than an omission.
    /// <para>
    /// One existed while the conflict phase could only draw a moving stripe — eighteen cold
    /// seconds with no way out is a hang with a logo. Counting the work removed the need:
    /// every phase reports a real fraction now, and a bar moving visibly through 214 / 292 is
    /// not something anyone needs to escape. If the stripe ever comes back, so should the
    /// button.
    /// </para>
    /// </summary>
    [Fact]
    public void The_wait_needs_no_escape_because_it_is_measured()
    {
        Hub.Should().NotContain("_skipLoadWait");
        Hub.Should().NotContain("SkipLoadPhase");
    }

    /// <summary>
    /// Every phase feeds the same progress channel, so the bar is determinate throughout.
    /// The settings copies materialise their file list before the first copy, because a bar
    /// whose total grows as it runs is worse than no bar at all — the rule
    /// <c>ScanProgress</c> already states for the scan.
    /// </summary>
    [Fact]
    public void Every_phase_reports_into_the_same_progress()
    {
        Hub.Should().Contain("progress: LoadProgress()",
            "the settings copies report per file");

        Hub.Should().Contain("_conflictAnalysis.Analyze(mods, version, gameDir, progress)",
            "and the conflict passes report per mod");
    }
}
