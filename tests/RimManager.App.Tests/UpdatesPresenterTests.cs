using System;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.App.Tests;

public sealed class UpdatesPresenterTests
{
    private static ModUpdateStatus Status(string name, UpdateStatus status) => new()
    {
        Id = ModId.From(name),
        Name = name,
        Status = status,
    };

    [Fact]
    public void Order_puts_updates_first_then_delisted_then_untracked_then_uptodate()
    {
        var input = new[]
        {
            Status("zed-uptodate", UpdateStatus.UpToDate),
            Status("beta-update", UpdateStatus.UpdateAvailable),
            Status("gamma-delisted", UpdateStatus.Delisted),
            Status("alpha-untracked", UpdateStatus.NotTracked),
            Status("alpha-update", UpdateStatus.UpdateAvailable),
        };

        var ordered = UpdatesPresenter.Order(input).Select(s => s.Name).ToArray();

        ordered.Should().Equal(
            "alpha-update", "beta-update",   // updates first, alphabetized
            "gamma-delisted",
            "alpha-untracked",
            "zed-uptodate");
    }

    [Fact]
    public void Summarize_lists_only_nonzero_categories()
    {
        var input = new[]
        {
            Status("a", UpdateStatus.UpdateAvailable),
            Status("b", UpdateStatus.UpdateAvailable),
            Status("c", UpdateStatus.Delisted),
            Status("d", UpdateStatus.UpToDate),
        };

        UpdatesPresenter.Summarize(input).Should().Be("2 updates · 1 delisted · 1 up to date");
    }

    [Fact]
    public void Summarize_singular_update_and_all_up_to_date()
    {
        UpdatesPresenter.Summarize([Status("a", UpdateStatus.UpdateAvailable)])
            .Should().Be("1 update");

        UpdatesPresenter.Summarize([Status("a", UpdateStatus.UpToDate), Status("b", UpdateStatus.NotTracked)])
            .Should().Be("All up to date (2 checked).");
    }

    [Fact]
    public void Summarize_handles_empty()
    {
        UpdatesPresenter.Summarize(Array.Empty<ModUpdateStatus>())
            .Should().Be("No Workshop mods to check.");
    }
}
