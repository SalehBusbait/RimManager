using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.App.Tests;

public sealed class CollectionPresenterTests
{
    private static CollectionMember Member(string id, bool installed, bool delisted = false) => new()
    {
        PublishedFileId = id,
        Title = $"mod {id}",
        InstalledPackageId = installed ? ModId.From($"pkg.{id}") : null,
        IsDelisted = delisted,
    };

    private static CollectionReport Report(params CollectionMember[] members) =>
        new() { Members = [.. members] };

    [Fact]
    public void Order_puts_missing_first_then_delisted_then_installed()
    {
        var report = Report(
            Member("1", installed: true),
            Member("2", installed: false),
            Member("3", installed: false, delisted: true),
            Member("4", installed: false));

        CollectionPresenter.Order(report.Members).Select(m => m.PublishedFileId)
            .Should().Equal("2", "4", "3", "1");
    }

    /// <summary>
    /// <c>2e</c> reconciles four ways, and the split that matters is between "present"
    /// and "already active": the same install, but one needs a click and the other
    /// needs nothing. A three-way model would offer to activate what is already there.
    /// </summary>
    [Fact]
    public void A_member_lands_in_exactly_one_of_the_four_states()
    {
        Row(Member("1", installed: false)).State.Should().Be(MemberState.ToDownload);
        Row(Member("2", installed: true)).State.Should().Be(MemberState.Present);
        Row(Member("3", installed: true), activeAt: 13).State.Should().Be(MemberState.AlreadyActive);
        Row(Member("4", installed: false, delisted: true)).State.Should().Be(MemberState.Unavailable);
    }

    [Fact]
    public void An_active_member_says_where_it_already_is()
    {
        var row = Row(Member("1", installed: true), activeAt: 13);

        row.StatusText.Should().Be("active");
        row.Note.Should().Be("already at #13");
        row.Action.Should().BeEmpty("there is nothing to do to a mod that is already loaded");
    }

    /// <summary>
    /// A live checkbox on a row nothing can be done to is a promise the app cannot
    /// keep — an unavailable item is gone from the Workshop.
    /// </summary>
    [Fact]
    public void Only_actionable_rows_can_be_ticked_and_only_downloads_start_ticked()
    {
        Row(Member("1", installed: false)).Should().Match<CollectionMemberRowViewModel>(
            r => r.CanSelect && r.IsSelected);
        Row(Member("2", installed: true)).Should().Match<CollectionMemberRowViewModel>(
            r => r.CanSelect && !r.IsSelected);
        Row(Member("3", installed: true), activeAt: 5).CanSelect.Should().BeFalse();
        Row(Member("4", installed: false, delisted: true)).CanSelect.Should().BeFalse();
    }

    [Fact]
    public void The_header_counts_add_up_to_the_whole_collection()
    {
        var rows = new[]
        {
            Row(Member("1", installed: false)),
            Row(Member("2", installed: false)),
            Row(Member("3", installed: true)),
            Row(Member("4", installed: true), activeAt: 7),
            Row(Member("5", installed: false, delisted: true)),
        };

        var (present, toDownload, unavailable, active) = CollectionPresenter.Reconcile(rows);

        (present, toDownload, unavailable, active).Should().Be((1, 2, 1, 1));
        (present + toDownload + unavailable + active).Should().Be(rows.Length);
    }

    private static CollectionMemberRowViewModel Row(CollectionMember member, int? activeAt = null) =>
        new(member, 1, activeAt);
}
