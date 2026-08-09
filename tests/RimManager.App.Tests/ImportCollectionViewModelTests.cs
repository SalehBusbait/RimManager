using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using FluentAssertions;
using RimManager.App.Services;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The wizard's own behaviour (<c>2i</c>-3): what it claims after a fetch, and the
/// rule that only the primary button counts as a decision.
/// </summary>
public sealed class ImportCollectionViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static CollectionMember Member(
        string id, bool installed = false, bool delisted = false) => new()
    {
        PublishedFileId = id,
        Title = $"mod {id}",
        InstalledPackageId = installed ? ModId.From($"pkg.{id}") : null,
        IsDelisted = delisted,
    };

    /// <summary>59 installed-but-inactive, 6 to download, 2 delisted, 1 already active.</summary>
    private static CollectionResolution Resolution(params CollectionMember[] members) =>
        new(true, "3184927733", "Anomaly Essentials",
            new CollectionReport { Members = [.. members] }, null, Now.AddDays(-3));

    private static ImportCollectionViewModel Wizard(
        CollectionResolution result,
        Func<ModId, int?>? activeAt = null,
        int wouldDeactivate = 0) =>
        new(_ => Task.FromResult(result),
            activeAt ?? (_ => null),
            _ => wouldDeactivate,
            () => Now);

    [Fact]
    public async Task Fetch_reports_the_four_way_reconcile()
    {
        var vm = Wizard(Resolution(
                Member("1", installed: true),
                Member("2"),
                Member("3", delisted: true),
                Member("4", installed: true)),
            activeAt: id => id.Value == "pkg.4" ? 87 : null);

        vm.Url = "3184927733";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.HasResolved.Should().BeTrue();
        vm.Collection.PresentCount.Should().Be(1);   // present but inactive
        vm.Collection.ToDownloadCount.Should().Be(1);
        vm.Collection.UnavailableCount.Should().Be(1);
        vm.Collection.AlreadyActiveCount.Should().Be(1);
        vm.ItemCount.Should().Be(4);
        vm.PrimaryLabel.Should().Be("Review 4 items →");
    }

    [Fact]
    public async Task Fetch_states_the_collection_and_when_it_changed()
    {
        var vm = Wizard(Resolution(Member("1")));

        vm.Url = "3184927733";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.ResolvedLine.Should().Be("Anomaly Essentials · 1 item · updated 3 days ago");
    }

    /// <summary>
    /// The number on the Replace radio is measured against the real load order, so it
    /// has to come back from the caller rather than be described in the abstract.
    /// </summary>
    [Fact]
    public async Task Fetch_measures_what_replace_would_cost()
    {
        var vm = Wizard(Resolution(Member("1")), wouldDeactivate: 155);

        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.ReplaceConsequence.Should().Contain("155 mods not in this collection");
    }

    [Fact]
    public async Task A_failed_fetch_says_why_and_resolves_nothing()
    {
        var vm = new ImportCollectionViewModel(
            _ => Task.FromResult(new CollectionResolution(
                false, "9", null, new CollectionReport { Members = [] },
                "Collection didn't resolve (deleted, private, or an item rather than a collection).")),
            _ => null, _ => 0, () => Now);

        vm.Url = "9";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.HasResolved.Should().BeFalse();
        vm.HasError.Should().BeTrue();
        vm.ErrorText.Should().Contain("didn't resolve");
    }

    [Fact]
    public async Task A_thrown_lookup_is_reported_rather_than_escaping()
    {
        var vm = new ImportCollectionViewModel(
            _ => throw new InvalidOperationException("socket closed"),
            _ => null, _ => 0, () => Now);

        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.HasError.Should().BeTrue();
        vm.IsFetching.Should().BeFalse();
    }

    /// <summary>
    /// The counts and the primary button would otherwise still describe the collection
    /// the user has just typed away from.
    /// </summary>
    [Fact]
    public async Task Editing_the_url_invalidates_the_previous_result()
    {
        var vm = Wizard(Resolution(Member("1")));
        vm.Url = "3184927733";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.Url = "3184927734";

        vm.HasResolved.Should().BeFalse();
        vm.Report.Should().BeNull();
    }

    [Fact]
    public void Cannot_fetch_an_empty_field()
    {
        var vm = Wizard(Resolution());

        vm.CanFetch.Should().BeFalse();
        vm.Url = "  ";
        vm.CanFetch.Should().BeFalse();
        vm.Url = "3184927733";
        vm.CanFetch.Should().BeTrue();
    }

    /// <summary>
    /// The test above passed while Fetch was <b>dead on arrival</b>: the value of
    /// CanFetch was right, but nothing raised PropertyChanged for it, so the button's
    /// IsEnabled never re-evaluated and could not be pressed.
    /// <para>
    /// Reading a computed property cannot see a missing notification. Anything a
    /// control's IsEnabled or IsVisible binds to has to be asserted this way instead.
    /// </para>
    /// </summary>
    [Fact]
    public void Typing_a_url_announces_that_Fetch_became_available()
    {
        var vm = Wizard(Resolution());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Url = "3184927733";

        raised.Should().Contain(nameof(ImportCollectionViewModel.CanFetch));
    }

    /// <summary>
    /// Resolving a 476-member collection is two round trips and takes over ten seconds.
    /// With only the button greying out, that reads as a dead button.
    /// </summary>
    [Fact]
    public async Task The_hint_gives_way_to_a_fetching_state_and_then_the_result()
    {
        var gate = new TaskCompletionSource<CollectionResolution>();
        var vm = new ImportCollectionViewModel(
            _ => gate.Task, _ => null, _ => 0, () => Now) { Url = "x" };

        vm.ShowsHint.Should().BeTrue();

        var fetching = vm.FetchCommand.ExecuteAsync(null);
        vm.IsFetching.Should().BeTrue();
        vm.ShowsHint.Should().BeFalse("the fetching line takes the slot");

        gate.SetResult(Resolution(Member("1")));
        await fetching;

        vm.IsFetching.Should().BeFalse();
        vm.ShowsHint.Should().BeFalse("the resolved line takes the slot");
    }

    [Fact]
    public void A_failed_fetch_announces_that_it_has_something_to_say()
    {
        var vm = new ImportCollectionViewModel(
            _ => Task.FromResult(new CollectionResolution(
                false, "9", null, new CollectionReport { Members = [] }, "nope")),
            _ => null, _ => 0, () => Now) { Url = "9" };

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.FetchCommand.Execute(null);

        raised.Should().Contain(nameof(ImportCollectionViewModel.HasError));
    }

    /// <summary>A count of a collection nobody has looked up yet is not a fact.</summary>
    [Fact]
    public void The_primary_states_no_count_before_anything_is_fetched() =>
        Wizard(Resolution()).PrimaryLabel.Should().Be("Review →");

    /// <summary>
    /// Everything the window binds must ANNOUNCE, not merely hold the right value.
    /// Step 2's subtree is built and bound at window load even though it starts
    /// hidden, so a property that is only assigned renders as whatever it was at
    /// construction — blank, for the collection title.
    /// </summary>
    [Fact]
    public async Task A_fetch_announces_every_property_the_window_binds()
    {
        // wouldDeactivate is non-zero so ReplaceConsequence genuinely changes:
        // ObservableProperty suppresses a notification for an unchanged value, which
        // is correct — the screen already shows it.
        var vm = Wizard(Resolution(Member("1"), Member("2", installed: true)), wouldDeactivate: 155);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Url = "3184927733";
        await vm.FetchCommand.ExecuteAsync(null);

        raised.Should().Contain(
        [
            nameof(ImportCollectionViewModel.HasResolved),
            nameof(ImportCollectionViewModel.ResolvedLine),
            nameof(ImportCollectionViewModel.ReplaceConsequence),
            nameof(ImportCollectionViewModel.Title),
            nameof(ImportCollectionViewModel.ItemCount),
            nameof(ImportCollectionViewModel.PrimaryLabel),
            nameof(ImportCollectionViewModel.CanCommit),
            nameof(ImportCollectionViewModel.StrategyRecap),
            nameof(ImportCollectionViewModel.InstalledShare),
            nameof(ImportCollectionViewModel.ToDownloadShare),
            nameof(ImportCollectionViewModel.UnavailableShare),
            nameof(ImportCollectionViewModel.AlreadyActiveShare),
        ]);
    }

    [Fact]
    public void Append_as_a_group_is_the_default_strategy()
    {
        var vm = Wizard(Resolution());

        vm.Collection.Strategy.Should().Be(ImportStrategy.AppendGroup);
        vm.Collection.IsAppendGroup.Should().BeTrue();
    }

    /// <summary>
    /// A radio group unchecks the outgoing member before checking the incoming one.
    /// Acting on that false would clear the selection.
    /// </summary>
    [Fact]
    public void Unchecking_a_strategy_radio_does_not_clear_the_choice()
    {
        var vm = Wizard(Resolution());

        vm.Collection.IsReplace = true;
        vm.Collection.IsAppendGroup = false;

        vm.Collection.Strategy.Should().Be(ImportStrategy.Replace);
    }

    /// <summary>
    /// Dismissal is never consent — the same rule as the destructive confirm. The
    /// window sets Accepted only from the primary; nothing else does.
    /// </summary>
    [Fact]
    public void A_wizard_is_unaccepted_until_something_says_otherwise() =>
        Wizard(Resolution()).Accepted.Should().BeFalse();

    // --- the two steps -------------------------------------------------------

    [Fact]
    public void A_fresh_wizard_starts_on_step_one_and_cannot_advance()
    {
        var vm = Wizard(Resolution());

        vm.IsStep1.Should().BeTrue();
        vm.CanCommit.Should().BeFalse("nothing has been fetched yet");
    }

    /// <summary>
    /// Review advances, it does not commit. `2i`-3's footer promises nothing happens
    /// until the next step, and this is the line that keeps it.
    /// </summary>
    [Fact]
    public async Task Review_advances_to_step_two_without_accepting()
    {
        var vm = Wizard(Resolution(Member("1")));
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.ReviewCommand.Execute(null);

        vm.IsStep2.Should().BeTrue();
        vm.Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task Back_returns_to_step_one()
    {
        var vm = Wizard(Resolution(Member("1")));
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);
        vm.ReviewCommand.Execute(null);

        vm.BackCommand.Execute(null);

        vm.IsStep1.Should().BeTrue();
    }

    /// <summary>
    /// Step 2's primary enumerates the two acts because they cost very different
    /// things — adding an installed mod is instant, a SteamCMD batch is minutes.
    /// </summary>
    [Fact]
    public async Task Step_two_names_both_halves_of_what_it_will_do()
    {
        var vm = Wizard(Resolution(
                Member("1", installed: true),   // present → "add"
                Member("2"),                    // missing → "download"
                Member("3")));
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);
        vm.ReviewCommand.Execute(null);

        // Missing rows start ticked; present ones do not (2e).
        vm.PrimaryLabel.Should().Be("Download 2 via SteamCMD");

        vm.Collection.Members.Single(m => m.IsPresent).IsSelected = true;
        vm.PrimaryLabel.Should().Be("Download 2 · add 1");
    }

    /// <summary>Replace is worth pressing with nothing ticked: clearing is the change.</summary>
    [Fact]
    public async Task Replace_can_commit_with_nothing_selected()
    {
        var vm = Wizard(Resolution(Member("1", installed: true)));
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);
        vm.ReviewCommand.Execute(null);
        vm.Collection.IsReplace = true;

        vm.CanCommit.Should().BeTrue();
        vm.PrimaryLabel.Should().Be("Replace the load order");
    }

    // --- the install route ---------------------------------------------------

    private static ImportCollectionViewModel WizardWithSteam(
        CollectionResolution result, bool steamRunning) =>
        new(_ => Task.FromResult(result), _ => null, _ => 0, () => Now,
            openCollectionPage: null, steamClientRunning: steamRunning);

    /// <summary>
    /// Measured, not fixed: subscribing is the better outcome when Steam can take the
    /// hand-off, and SteamCMD is the only one that works when it cannot.
    /// </summary>
    [Fact]
    public void The_route_defaults_to_whichever_can_actually_run()
    {
        WizardWithSteam(Resolution(), steamRunning: true).Route
            .Should().Be(ImportRoute.SubscribeInSteam);
        WizardWithSteam(Resolution(), steamRunning: false).Route
            .Should().Be(ImportRoute.SteamCmd);
    }

    /// <summary>A control that governs nothing is worse than its absence.</summary>
    [Fact]
    public async Task The_route_is_hidden_when_nothing_needs_fetching()
    {
        var vm = Wizard(Resolution(Member("1", installed: true)));
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.ShowsRoute.Should().BeFalse();
    }

    [Fact]
    public async Task The_route_is_offered_when_something_does()
    {
        var vm = Wizard(Resolution(Member("1")));
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);

        vm.ShowsRoute.Should().BeTrue();
    }

    /// <summary>Switching route rewrites the primary, because it changes what it does.</summary>
    [Fact]
    public async Task Switching_route_renames_the_primary()
    {
        var vm = WizardWithSteam(Resolution(Member("1"), Member("2")), steamRunning: false);
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);
        vm.ReviewCommand.Execute(null);

        vm.PrimaryLabel.Should().Be("Download 2 via SteamCMD");

        vm.IsSubscribeRoute = true;

        vm.PrimaryLabel.Should().Be("Subscribe to all 2 in Steam");
    }

    [Fact]
    public void Unchecking_a_route_radio_does_not_clear_the_choice()
    {
        var vm = WizardWithSteam(Resolution(), steamRunning: false);

        vm.IsSubscribeRoute = true;
        vm.IsSteamCmdRoute = false;

        vm.Route.Should().Be(ImportRoute.SubscribeInSteam);
    }

    [Fact]
    public async Task Editing_the_url_on_step_two_sends_you_back_to_step_one()
    {
        var vm = Wizard(Resolution(Member("1")));
        vm.Url = "x";
        await vm.FetchCommand.ExecuteAsync(null);
        vm.ReviewCommand.Execute(null);

        vm.Url = "y";

        vm.IsStep1.Should().BeTrue();
    }
}
