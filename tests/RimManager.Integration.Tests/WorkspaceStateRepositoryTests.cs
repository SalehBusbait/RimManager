using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Workshop;
using RimManager.Storage;
using RimManager.Storage.Repositories;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Per-instance layout, snoozes and rule overrides, round-tripped through real files.
/// </summary>
public sealed class WorkspaceStateRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rimmanager-state-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly PhysicalFileSystem _fs = new();

    public WorkspaceStateRepositoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private WorkspaceStateRepository Repo() => new(_fs, _dir);

    // --- layout --------------------------------------------------------------

    /// <summary>A missing file is the normal first-run case, never an error.</summary>
    [Fact]
    public void Layout_defaults_when_nothing_is_saved()
    {
        var layout = Repo().LoadLayout();

        layout.IsDockOpen.Should().BeFalse("the dock is closed by default; only the 26px strip shows");
        layout.DockTab.Should().Be("warnings");
        layout.WarningsOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Layout_round_trips()
    {
        var saved = LayoutState.Default with
        {
            IsDockOpen = true,
            DockTab = "updates",
            ActiveTagFilters = ["t1", "t2"],
            MatchAllTags = true,
            WarningsOnly = true,
        };

        await Repo().SaveLayoutAsync(saved);
        var loaded = Repo().LoadLayout();

        loaded.Should().BeEquivalentTo(saved);
    }

    /// <summary>
    /// O4 · ONE dock height for every tab, and detail widths still per tab. This test
    /// used to assert the opposite; it is rewritten rather than deleted so the new model
    /// ships with a guard, which is what the old one was for.
    /// </summary>
    [Fact]
    public async Task One_dock_height_is_shared_and_detail_widths_stay_per_tab()
    {
        var saved = LayoutState.Default with
        {
            DockHeight = 380,
            DockDetailWidths = LayoutState.Default.DockDetailWidths
                .SetItem("updates", 300).SetItem("history", 640),
        };

        await Repo().SaveLayoutAsync(saved);
        var loaded = Repo().LoadLayout();

        loaded.DockHeight.Should().Be(380);
        loaded.DockDetailWidths["updates"].Should().Be(300);
        loaded.DockDetailWidths["history"].Should().Be(640,
            "History is three panes, so its detail column is genuinely its own measurement");
        loaded.DockDetailWidths.ContainsKey("warnings").Should().BeFalse(
            "an unvisited tab has no entry and falls back to the designed default");
    }

    /// <summary>
    /// O17 · the window's own geometry round-trips, including the flag that says the
    /// bounds beside it are the RESTORED ones rather than the maximised rectangle.
    /// </summary>
    [Fact]
    public async Task The_window_geometry_round_trips()
    {
        var saved = LayoutState.Default with
        {
            WindowX = -1280,
            WindowY = 40,
            WindowWidth = 1600,
            WindowHeight = 980,
            WindowMaximised = true,
            InactivePaneWidth = 412,
            InfoPaneWidth = 360,
        };

        await Repo().SaveLayoutAsync(saved);
        var loaded = Repo().LoadLayout();

        loaded.WindowX.Should().Be(-1280, "a monitor to the left of the primary is a negative X");
        loaded.WindowY.Should().Be(40);
        loaded.WindowWidth.Should().Be(1600);
        loaded.WindowHeight.Should().Be(980);
        loaded.WindowMaximised.Should().BeTrue();
        loaded.InactivePaneWidth.Should().Be(412);
        loaded.InfoPaneWidth.Should().Be(360);
    }

    /// <summary>
    /// A first run, and every launch before the first close: nothing saved, and the
    /// window has to fall back to centring itself rather than to zeroes.
    /// </summary>
    [Fact]
    public void An_unwritten_layout_carries_no_window_geometry()
    {
        var fresh = Repo().LoadLayout();

        fresh.WindowX.Should().BeNull();
        fresh.WindowWidth.Should().BeNull();
        fresh.DockHeight.Should().BeNull();
        fresh.WindowMaximised.Should().BeFalse();
    }

    // --- snoozes -------------------------------------------------------------

    [Fact]
    public async Task Snoozes_round_trip_with_their_recorded_versions()
    {
        var id = ModId.From("brrainz.cameraplus");
        var at = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        await Repo().SaveSnoozesAsync(SnoozeSet.Empty.With(
            new ModSnooze(id, SnoozeKind.UntilNextGameVersion, at, AtGameVersion: "1.6")));

        var loaded = Repo().LoadSnoozes().For(id);

        loaded.Should().NotBeNull();
        loaded!.Kind.Should().Be(SnoozeKind.UntilNextGameVersion);
        loaded.AtGameVersion.Should().Be("1.6");
        loaded.IsActive(at.AddYears(1), "9.9", "1.6").Should().BeTrue();
    }

    [Fact]
    public void Snoozes_default_to_empty() => Repo().LoadSnoozes().Entries.Should().BeEmpty();

    // --- rule overrides ------------------------------------------------------

    /// <summary>
    /// A disabled community rule is a record, not a deletion (2i-5) — so it has to
    /// survive a restart intact, or the next resync silently resurrects it.
    /// </summary>
    [Fact]
    public async Task Rule_overrides_round_trip()
    {
        var saved = RuleOverrides.Empty
            .WithUserRule(new UserRule(ModId.From("a.mod"), ModId.From("b.mod"), "mine"))
            .Disable(ModId.From("c.mod"), ModId.From("d.mod"), "wrong for my list");

        await Repo().SaveRuleOverridesAsync(saved);
        var loaded = Repo().LoadRuleOverrides();

        loaded.UserRules.Should().ContainSingle().Which.Comment.Should().Be("mine");
        loaded.IsDisabled(ModId.From("c.mod"), ModId.From("d.mod")).Should().BeTrue();
        loaded.Disabled.Single().Reason.Should().Be("wrong for my list");
        loaded.OverrideCount.Should().Be(2);
    }

    [Fact]
    public void Rule_overrides_default_to_empty() => Repo().LoadRuleOverrides().IsEmpty.Should().BeTrue();

    /// <summary>
    /// Three files, not one blob: layout changes on every splitter drag while rules
    /// change only in the rule editor, so a layout write must never be able to
    /// endanger hand-authored rules.
    /// </summary>
    [Fact]
    public async Task The_three_kinds_live_in_separate_files()
    {
        var repo = Repo();
        await repo.SaveLayoutAsync(LayoutState.Default with { IsDockOpen = true });
        await repo.SaveRuleOverridesAsync(
            RuleOverrides.Empty.WithUserRule(new UserRule(ModId.From("a"), ModId.From("b"))));

        File.Exists(Path.Combine(_dir, "layout.json")).Should().BeTrue();
        File.Exists(Path.Combine(_dir, "rules.json")).Should().BeTrue();

        // Rewriting layout leaves the rules file untouched.
        var rulesBefore = File.ReadAllText(Path.Combine(_dir, "rules.json"));
        await repo.SaveLayoutAsync(LayoutState.Default with { IsDockOpen = false });

        File.ReadAllText(Path.Combine(_dir, "rules.json")).Should().Be(rulesBefore);
    }
}
