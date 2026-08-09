using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The first-run wizard's chain and wording (<c>2j</c>), and the separator groups it
/// proposes — which are counts on screen the user is asked to accept, so they are
/// derived from a real sort rather than guessed.
/// </summary>
public sealed class FirstRunPresenterTests
{
    private static SortResult Sorted(params (string Id, Tier Tier)[] mods)
    {
        var order = mods.Select(m => ModId.From(m.Id)).ToImmutableArray();
        var tiers = mods.ToImmutableDictionary(m => ModId.From(m.Id), m => m.Tier);
        return new SortResult(order, tiers, [], [], [], []);
    }

    // --- the progress chain ---------------------------------------------------

    [Fact]
    public void The_chain_reads_done_current_upcoming()
    {
        FirstRunPresenter.NodeState(0, FirstRunStep.Modlist).Should().Be(ChainNodeState.Done);
        FirstRunPresenter.NodeState(1, FirstRunStep.Modlist).Should().Be(ChainNodeState.Done);
        FirstRunPresenter.NodeState(2, FirstRunStep.Modlist).Should().Be(ChainNodeState.Current);
        FirstRunPresenter.NodeState(3, FirstRunStep.Modlist).Should().Be(ChainNodeState.Upcoming);
    }

    [Fact]
    public void On_the_first_step_nothing_is_done_yet() =>
        Enumerable.Range(0, 4).Select(i => FirstRunPresenter.NodeState(i, FirstRunStep.Welcome))
            .Should().Equal(
                ChainNodeState.Current, ChainNodeState.Upcoming,
                ChainNodeState.Upcoming, ChainNodeState.Upcoming);

    /// <summary>The last step's primary opens the app; the others advance.</summary>
    [Fact]
    public void Only_the_last_step_offers_to_open_the_app()
    {
        FirstRunPresenter.PrimaryLabel(FirstRunStep.Welcome).Should().Be("Get started →");
        FirstRunPresenter.PrimaryLabel(FirstRunStep.Paths).Should().Be("Continue");
        FirstRunPresenter.PrimaryLabel(FirstRunStep.Modlist).Should().Be("Continue");
        FirstRunPresenter.PrimaryLabel(FirstRunStep.Rules).Should().Be("Open RimManager");
    }

    /// <summary>
    /// The wizard's whole promise. Step 3 is where the import has just been read, so
    /// that is where it has to be said in those words.
    /// </summary>
    [Fact]
    public void The_instance_step_promises_nothing_has_been_written() =>
        FirstRunPresenter.FooterHint(FirstRunStep.Modlist)
            .Should().Be("Still nothing written to your game folder.");

    // --- the proposed groups --------------------------------------------------

    [Fact]
    public void Groups_follow_load_order_and_count_their_mods()
    {
        var result = Sorted(
            ("ludeon.rimworld", Tier.Core),
            ("ludeon.royalty", Tier.Dlc),
            ("brrainz.harmony", Tier.PreCore),
            ("a.mod", Tier.Normal),
            ("b.mod", Tier.Normal),
            ("z.patch", Tier.Bottom));

        FirstRunPresenter.ProposedGroups(result)
            .Select(g => (g.Name, g.Count))
            .Should().Equal(
                ("Core & DLC", 2), ("Load before Core", 1), ("Mods", 2), ("Load last", 1));
    }

    /// <summary>
    /// Core and DLC are one group, so two adjacent tiers with the same name must not
    /// become two separators — the same coalescing Auto-layout does.
    /// </summary>
    [Fact]
    public void Adjacent_tiers_sharing_a_name_are_one_group() =>
        FirstRunPresenter.ProposedGroups(Sorted(
                ("core", Tier.Core), ("dlc1", Tier.Dlc), ("dlc2", Tier.Dlc)))
            .Should().ContainSingle().Which.Count.Should().Be(3);

    [Fact]
    public void An_empty_sort_proposes_nothing() =>
        FirstRunPresenter.ProposedGroups(Sorted()).Should().BeEmpty();

    /// <summary>A group's hue must not depend on where it lands in the list.</summary>
    [Fact]
    public void A_group_keeps_its_hue_wherever_it_appears() =>
        FirstRunPresenter.PaletteIndexFor("Core & DLC")
            .Should().Be(FirstRunPresenter.PaletteIndexFor("Core & DLC"))
            .And.NotBe(FirstRunPresenter.PaletteIndexFor("Load last"));

    // --- the sources line -----------------------------------------------------

    [Fact]
    public void The_sources_line_lists_what_is_there()
    {
        var counts = new Dictionary<ModSource, int>
        {
            [ModSource.Dlc] = 4,
            [ModSource.Workshop] = 324,
            [ModSource.Local] = 14,
        };

        FirstRunPresenter.SourcesLine(counts).Should().Be("4 DLC · 324 Workshop · 14 local");
    }

    /// <summary>
    /// "0 Workshop" on a GOG install is noise about something that will never apply,
    /// so an absent source is absent rather than zero.
    /// </summary>
    [Fact]
    public void A_source_with_nothing_in_it_is_not_mentioned() =>
        FirstRunPresenter.SourcesLine(new Dictionary<ModSource, int>
        {
            [ModSource.Local] = 14,
            [ModSource.Workshop] = 0,
        }).Should().Be("14 local");

    [Fact]
    public void Nothing_at_all_reads_as_a_dash() =>
        FirstRunPresenter.SourcesLine(new Dictionary<ModSource, int>()).Should().Be("—");
}
