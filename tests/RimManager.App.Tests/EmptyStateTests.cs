using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Validation;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Filtered-empty is not the success state (T2, v2 systemic pass). Both dock tabs
/// shipped the false version: the "all clean" overlay bound to the FILTERED list,
/// so a chip or search that hid every row asserted a clean install while real
/// warnings existed behind the filter.
/// </summary>
public sealed class EmptyStateTests
{
    private static WarningsViewModel Panel(params ValidationIssue[] issues)
    {
        var vm = new WarningsViewModel();
        vm.Populate(issues, [], _ => null, new Dictionary<ModId, string>(), "test");
        return vm;
    }

    private static ValidationIssue Warning() => new(
        ValidationSeverity.Warning, IssueCodes.OrderViolated,
        "'a' should load before 'b'.",
        ModId.From("a.mod"), ModId.From("b.mod"), DeclaredBy: ModId.From("a.mod"));

    [Fact]
    public void No_warnings_at_all_is_truly_empty()
    {
        var panel = Panel();

        panel.IsTrulyEmpty.Should().BeTrue();
        panel.IsFilteredEmpty.Should().BeFalse();
    }

    [Fact]
    public void A_filter_that_hides_everything_is_not_the_success_state()
    {
        var panel = Panel(Warning());

        panel.ShowBlocking = true;   // the one warning is Warning-toned, so zero rows

        panel.Rows.Should().BeEmpty("the chip filtered the only warning out");
        panel.IsTrulyEmpty.Should().BeFalse(
            "a hidden warning is still a warning — claiming a clean list here is the shipped bug");
        panel.IsFilteredEmpty.Should().BeTrue();
        panel.FilteredEmptyText.Should().Contain("1 warning",
            "the state says how much the filter hides (the 2c rule)");
    }

    [Fact]
    public void A_search_that_misses_is_not_the_success_state()
    {
        var panel = Panel(Warning());

        panel.Search = "zzz-no-such-mod";

        panel.IsFilteredEmpty.Should().BeTrue();
        panel.IsTrulyEmpty.Should().BeFalse();
    }

    [Fact]
    public void Clear_filters_is_the_way_back()
    {
        var panel = Panel(Warning());
        panel.Search = "zzz";
        panel.ShowBlocking = true;

        panel.ClearFiltersCommand.Execute(null);

        panel.ShowAll.Should().BeTrue();
        panel.Search.Should().BeEmpty();
        panel.Rows.Should().NotBeEmpty();
    }

    /// <summary>
    /// The markup half: the success overlays must bind the truly-empty flags, because
    /// binding <c>!Rows.Count</c> — the filtered list — is exactly the defect. A VM
    /// property nothing binds is the half-wired shape this repo has shipped before.
    /// </summary>
    [Fact]
    public void The_success_overlays_bind_truly_empty_not_the_filtered_list()
    {
        var markup = File.ReadAllText(Path.Combine(RepoPaths.AppProject, "MainWindow.axaml"));

        markup.Should().Contain("{Binding WarningsPanel.IsTrulyEmpty}");
        markup.Should().Contain("{Binding WarningsPanel.IsFilteredEmpty}");
        markup.Should().Contain("{Binding History.IsTrulyEmpty}");
        markup.Should().Contain("{Binding History.IsFilteredEmpty}");
        markup.Should().NotContain("{Binding !WarningsPanel.Rows.Count}",
            "the filtered list is not the install's state");
        markup.Should().NotContain("{Binding !History.Rows.Count}");
    }
}
