using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// <c>2i</c>-3 step 1. The sentences here are claims the buttons downstream have to
/// honour, which is why they are pinned rather than eyeballed.
/// </summary>
public sealed class ImportCollectionPresenterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static ModRowViewModel Mod(string id, string? fileId = null,
        ModSource source = ModSource.Workshop) =>
        new(new Mod
        {
            PackageId = ModId.From(id),
            Name = id,
            Source = source,
            RootPath = "/" + id,
            PublishedFileId = fileId,
        });

    private static SeparatorRowViewModel Sep(string name) => new($"sep-{name}", name);

    // --- the resolved line ---------------------------------------------------

    [Fact]
    public void Resolved_names_the_collection_its_size_and_when_it_changed() =>
        ImportCollectionPresenter.Resolved("Anomaly Essentials", 68, Now.AddDays(-3), Now)
            .Should().Be("Anomaly Essentials · 68 items · updated 3 days ago");

    /// <summary>
    /// Steam's keyless metadata has no update time for some items, and a date invented
    /// there would read as fact — the same reason <c>2b</c>'s LATEST column is a dash.
    /// </summary>
    [Fact]
    public void Resolved_says_nothing_about_time_when_Steam_did_not() =>
        ImportCollectionPresenter.Resolved("Anomaly Essentials", 68, null, Now)
            .Should().Be("Anomaly Essentials · 68 items");

    [Fact]
    public void Resolved_falls_back_when_the_collection_has_no_title() =>
        ImportCollectionPresenter.Resolved("  ", 1, null, Now)
            .Should().Be("Untitled collection · 1 item");

    [Fact]
    public void ReviewLabel_agrees_in_number() =>
        (ImportCollectionPresenter.ReviewLabel(68), ImportCollectionPresenter.ReviewLabel(1))
            .Should().Be(("Review 68 items →", "Review 1 item →"));

    // --- the replace consequence ---------------------------------------------

    [Fact]
    public void ReplaceConsequence_names_what_goes_and_what_stays()
    {
        var line = ImportCollectionPresenter.ReplaceConsequence(155);

        line.Should().Contain("155 mods not in this collection");
        line.Should().Contain("Core and DLC stay");
        line.Should().Contain("Reversible via snapshot");
    }

    [Fact]
    public void ReplaceConsequence_does_not_threaten_when_there_is_nothing_to_remove() =>
        ImportCollectionPresenter.ReplaceConsequence(0)
            .Should().Be("Nothing would be deactivated — everything you have loaded is already in this collection.");

    // --- the install route ---------------------------------------------------

    /// <summary>
    /// Steam's "Subscribe to all" takes the WHOLE collection — it has no notion of our
    /// checkboxes — so the button must quote the collection, not the selection. A
    /// primary reading "Subscribe 343" would be describing something that is not going
    /// to happen.
    /// </summary>
    [Fact]
    public void The_subscribe_route_quotes_the_collection_not_the_selection() =>
        ImportCollectionPresenter.CommitLabel(
                download: 343, add: 0, ImportStrategy.AppendGroup,
                ImportRoute.SubscribeInSteam, totalItems: 476)
            .Should().Be("Subscribe to all 476 in Steam");

    [Fact]
    public void The_subscribe_route_still_names_what_joins_the_load_order() =>
        ImportCollectionPresenter.CommitLabel(
                download: 343, add: 59, ImportStrategy.AppendGroup,
                ImportRoute.SubscribeInSteam, totalItems: 476)
            .Should().Be("Subscribe to all 476 · add 59");

    /// <summary>SteamCMD is the only route that honours a partial selection.</summary>
    [Fact]
    public void The_SteamCMD_route_quotes_the_selection() =>
        ImportCollectionPresenter.CommitLabel(
                download: 343, add: 0, ImportStrategy.AppendGroup,
                ImportRoute.SteamCmd, totalItems: 476)
            .Should().Be("Download 343 via SteamCMD");

    /// <summary>With nothing to fetch the route governs nothing, so it cannot change the label.</summary>
    [Fact]
    public void With_nothing_to_fetch_the_route_makes_no_difference()
    {
        var viaSteam = ImportCollectionPresenter.CommitLabel(
            0, 59, ImportStrategy.AppendGroup, ImportRoute.SubscribeInSteam, 476);
        var viaCmd = ImportCollectionPresenter.CommitLabel(
            0, 59, ImportStrategy.AppendGroup, ImportRoute.SteamCmd, 476);

        viaSteam.Should().Be("Add 59 to the load order").And.Be(viaCmd);
    }

    /// <summary>NF-10 coherence: the fourth strategy names its landing — a new
    /// modlist, never the current order.</summary>
    [Fact]
    public void The_new_modlist_strategy_names_its_landing()
    {
        ImportCollectionPresenter.CommitLabel(
                0, 59, ImportStrategy.NewModlist, ImportRoute.SteamCmd, 476)
            .Should().Be("New modlist of 59");

        ImportCollectionPresenter.CommitLabel(
                343, 59, ImportStrategy.NewModlist, ImportRoute.SteamCmd, 476)
            .Should().Be("Download 343 · new modlist of 59");
    }

    // --- what replace would actually take out --------------------------------

    [Fact]
    public void WouldDeactivate_takes_the_mods_the_collection_does_not_name()
    {
        List<RowViewModel> active =
        [
            Sep("Frameworks"),
            Mod("in.the.collection", "111"),
            Mod("not.in.the.collection", "222"),
        ];

        ImportCollectionPresenter.WouldDeactivate(active, new HashSet<string> { "111" })
            .Select(r => r.PackageId.Value)
            .Should().Equal("not.in.the.collection");
    }

    /// <summary>
    /// A literal reading of "not in this collection" would take out Core — no Workshop
    /// collection lists it — and the game would not start. The anchors are exempt on
    /// the most destructive of the three strategies precisely because it is.
    /// </summary>
    [Fact]
    public void WouldDeactivate_never_takes_Core_or_DLC()
    {
        List<RowViewModel> active =
        [
            Mod("ludeon.rimworld", source: ModSource.Core),
            Mod("ludeon.rimworld.royalty", source: ModSource.Dlc),
            Mod("ordinary.mod", "222"),
        ];

        ImportCollectionPresenter.WouldDeactivate(active, new HashSet<string>())
            .Select(r => r.PackageId.Value)
            .Should().Equal("ordinary.mod");
    }

    /// <summary>A local mod has no Workshop id, so no collection can name it.</summary>
    [Fact]
    public void WouldDeactivate_counts_a_local_mod_as_not_in_the_collection()
    {
        List<RowViewModel> active = [Mod("hand.written", source: ModSource.Local)];

        ImportCollectionPresenter.WouldDeactivate(active, new HashSet<string> { "111" })
            .Should().ContainSingle();
    }

    [Fact]
    public void WouldDeactivate_ignores_separators()
    {
        List<RowViewModel> active = [Sep("Frameworks"), Sep("Content")];

        ImportCollectionPresenter.WouldDeactivate(active, new HashSet<string>())
            .Should().BeEmpty();
    }

    // --- the bar -------------------------------------------------------------

    /// <summary>
    /// Proportional to the counts printed beside it. The mockup's bar is drawn to a
    /// scale its own figures do not support; a bar that disagrees with its numbers is
    /// worse than no bar.
    /// </summary>
    [Fact]
    public void BarShares_are_the_counts_themselves() =>
        ImportCollectionPresenter.BarShares(59, 6, 2, 1).Should().Equal(59, 6, 2, 1);

    [Fact]
    public void BarShares_collapse_to_nothing_when_there_is_nothing() =>
        ImportCollectionPresenter.BarShares(0, 0, 0, 0).Should().Equal(0, 0, 0, 0);
}
