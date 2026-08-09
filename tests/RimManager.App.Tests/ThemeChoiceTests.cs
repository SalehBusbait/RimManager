using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using FluentAssertions;
using RimManager.App.Tests.Fakes;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The theme has exactly ONE store, and Save does not touch it.
/// <para>
/// Both halves are regressions that shipped. There were two stores — this interface's
/// flag and a <c>ThemeIndex</c> field on the Settings view model — and neither knew about
/// the other, so:
/// </para>
/// <list type="number">
///   <item>the Appearance page's Light/Dark radios wrote a bool that nothing applied, and
///   therefore did nothing at all;</item>
///   <item><c>Save</c> re-applied the index captured when the window opened, silently
///   reverting a theme the user had changed from the View menu since.</item>
/// </list>
/// <para>
/// Both built clean and passed every test. Neither is visible without changing the theme
/// and then saving a path.
/// </para>
/// </summary>
public sealed class ThemeChoiceTests
{
    private static (SettingsViewModel Vm, FakePreferences Prefs) Build()
    {
        var (vm, prefs, _) = SettingsHarness.Build();
        return (vm, prefs);
    }

    private static ThemeChoiceViewModel Row(SettingsViewModel vm, AppTheme theme) =>
        vm.ThemeChoices.Single(c => c.Theme == theme);

    [Theory]
    [InlineData(AppTheme.DropPodsLight)]
    [InlineData(AppTheme.DropPodsDark)]
    [InlineData(AppTheme.Ice)]
    [InlineData(AppTheme.FollowSystem)]
    public void Choosing_a_theme_writes_the_one_store(AppTheme chosen)
    {
        var (vm, prefs) = Build();
        prefs.Theme = chosen == AppTheme.DropPodsDark ? AppTheme.DropPodsLight : AppTheme.DropPodsDark;

        Row(vm, chosen).ChooseCommand.Execute(null);

        prefs.Theme.Should().Be(chosen,
            "the row must write through to the live setting, not to a copy of it");
    }

    /// <summary>
    /// Every option is separately represented and mirrors the one store — the
    /// v1 bug was a bound option whose siblings could not be clicked at all.
    /// </summary>
    [Fact]
    public void Every_option_reports_its_own_state()
    {
        var (vm, prefs) = Build();

        vm.ThemeChoices.Should().HaveCount(11, "follow-system plus the ten-theme roster");

        foreach (var target in new[] { AppTheme.DropPodsDark, AppTheme.Toxic, AppTheme.FollowSystem })
        {
            prefs.Theme = target;
            vm.ThemeChoices.Where(c => c.IsSelected).Should().ContainSingle()
                .Which.Theme.Should().Be(target);
        }
    }

    /// <summary>
    /// Selection state is display only — when it was a trigger (the radio-group
    /// shape), everything a group checks uninvited wrote the preference. The
    /// log-level bug, kept out of the theme list by this pin.
    /// </summary>
    [Fact]
    public void Selection_state_alone_fires_nothing()
    {
        var (vm, prefs) = Build();
        var before = prefs.Theme;

        Row(vm, AppTheme.Anomaly).IsSelected = true;

        prefs.Theme.Should().Be(before, "IsSelected is written by the resync, never a writer itself");
    }

    /// <summary>
    /// Follow-system is its own state, not the absence of a choice. A bool store
    /// cannot hold it, which is the reason the enum exists.
    /// </summary>
    [Fact]
    public void Follow_system_is_not_the_same_as_a_light_theme()
    {
        var (vm, prefs) = Build();

        Row(vm, AppTheme.FollowSystem).ChooseCommand.Execute(null);
        prefs.Theme.Should().Be(AppTheme.FollowSystem);
        Row(vm, AppTheme.DropPodsLight).IsSelected.Should().BeFalse();
    }

    /// <summary>
    /// The second bug, pinned against the mechanism that replaced it. Save is gone, so a
    /// path now commits as it is edited — and that write must still leave the theme alone.
    /// The original defect re-applied the index captured when the window opened, so any
    /// path commit silently reverted a theme chosen from the View menu since.
    /// </summary>
    [Fact]
    public async Task Committing_a_path_never_touches_the_theme()
    {
        var (vm, prefs, saved) = SettingsHarness.Build();

        Row(vm, AppTheme.DropPodsLight).ChooseCommand.Execute(null);
        var afterChoosing = prefs.Applied;

        vm.GameDir = "/elsewhere";
        await vm.FlushPathsAsync();

        saved.Should().NotBeEmpty("the edit is the commit — there is no button left to press");
        prefs.Theme.Should().Be(AppTheme.DropPodsLight);
        prefs.Applied.Should().Be(afterChoosing,
            "a path write touches the paths; re-applying a theme captured at open time is "
            + "how a theme chosen from the menu got silently reverted");
    }

    /// <summary>
    /// Re-selecting the current theme is not a change, so it must not churn — a settings
    /// page that reapplies the theme on every property notification would flicker.
    /// </summary>
    [Fact]
    public void Choosing_the_theme_already_in_use_changes_nothing()
    {
        var (vm, prefs) = Build();

        Row(vm, AppTheme.DropPodsDark).ChooseCommand.Execute(null);
        var applied = prefs.Applied;

        Row(vm, AppTheme.DropPodsDark).ChooseCommand.Execute(null);

        prefs.Applied.Should().Be(applied);
    }

    // --- source guards ------------------------------------------------------
    // The tests above pin the STORE. They cannot pin the APPLICATION of it, because
    // Application.Current is null in a headless test and every code path that writes
    // RequestedThemeVariant returns early. That is precisely the gap both original bugs
    // lived in — the store was written correctly and simply never reached the screen.
    // So the application side is guarded at the source, which is checkable.

    /// <summary>
    /// Exactly one place in the app writes <c>RequestedThemeVariant</c>. A second writer is
    /// a second theme store by another name: that is what <c>SettingsViewModel.ApplyTheme</c>
    /// was, and it re-applied a stale value on every Save.
    /// </summary>
    [Fact]
    public void Only_one_place_applies_the_theme()
    {
        var writers = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (file.Contains($"{sep}obj{sep}") || file.Contains($"{sep}bin{sep}")) continue;

            var text = File.ReadAllText(file);
            var count = text.Split("RequestedThemeVariant =").Length - 1;
            for (var i = 0; i < count; i++) writers.Add(Path.GetFileName(file));
        }

        writers.Should().ContainSingle(
            "the theme is applied in exactly one place — MainWindowViewModel.ApplyTheme — "
            + "so no surface can change the stored theme without it taking effect")
            .Which.Should().Be("MainWindowViewModel.cs");
    }

    /// <summary>
    /// And that one place is reached by <b>setting the stored theme</b>. Without this the
    /// store and the screen come apart again: the Appearance radios wrote the store, only
    /// View ▸ Theme applied it, and so choosing Light did nothing whatsoever.
    /// </summary>
    [Fact]
    public void Setting_the_stored_theme_is_what_applies_it()
    {
        var source = RepoPaths.HubSource();

        // Pinned as two facts rather than one exact line: the handler grew a body when the
        // accent began re-deriving on a theme change, and an exact-text guard failed on a
        // correct edit. What must hold is that the handler exists and applies the theme.
        source.Should().Contain("partial void OnThemeChanged(AppTheme value)",
            "assigning Theme must be the thing that applies it — otherwise a caller can "
            + "store a theme that never reaches the screen");
        source.Should().Contain("ApplyTheme(value)",
            "and the handler has to actually apply it");
    }
}
