using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Validation;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The dependency resolver's planner (<c>2i</c>-4). The deduplication is the point: the
/// validator reports one issue per <i>requiring mod</i>, so four mods wanting Harmony is
/// four warnings — but one problem, and one card.
/// </summary>
public sealed class DependencyResolverTests
{
    private static ModId Id(string s) => ModId.From(s);

    private static Mod ModOf(string id, string name, string? workshopId = null) => new()
    {
        PackageId = Id(id),
        Name = name,
        Source = ModSource.Workshop,
        RootPath = "/mods/" + id,
        PublishedFileId = workshopId,
    };

    private static ValidationIssue Missing(string requester, string needed) =>
        new(ValidationSeverity.Error, IssueCodes.MissingDependency, "x", Id(requester), Id(needed));

    private static ValidationIssue MissingDlc(string requester, string dlc) =>
        new(ValidationSeverity.Error, IssueCodes.MissingDlc, "x", Id(requester), Id(dlc));

    private static ImmutableArray<DependencyCard> Plan(
        IEnumerable<ValidationIssue> issues,
        Dictionary<ModId, Mod>? installed = null,
        HashSet<ModId>? active = null,
        IReadOnlyCollection<ModId>? owned = null,
        Dictionary<ModId, ModDependency>? declared = null) =>
        DependencyResolver.Plan(issues, installed ?? [], active ?? [], owned ?? [], declared ?? []);

    /// <summary>
    /// The reason this class exists. Without grouping the dialog shows "Harmony" three
    /// times and three warnings look like three problems.
    /// </summary>
    [Fact]
    public void One_missing_dependency_wanted_by_three_mods_is_one_card()
    {
        var installed = new Dictionary<ModId, Mod>
        {
            [Id("a.one")] = ModOf("a.one", "Alpha"),
            [Id("b.two")] = ModOf("b.two", "Bravo"),
            [Id("c.three")] = ModOf("c.three", "Charlie"),
        };

        var cards = Plan(
            [Missing("a.one", "brrainz.harmony"),
             Missing("b.two", "brrainz.harmony"),
             Missing("c.three", "brrainz.harmony")],
            installed);

        cards.Should().ContainSingle();
        cards[0].RequiredBy.Should().HaveCount(3);
        cards[0].RequiredByText.Should().Be("Required by Alpha and 2 others");
    }

    [Fact]
    public void The_required_by_line_reads_correctly_at_one_and_two()
    {
        var installed = new Dictionary<ModId, Mod> { [Id("a.one")] = ModOf("a.one", "Alpha") };

        Plan([Missing("a.one", "x.y")], installed)[0].RequiredByText
            .Should().Be("Required by Alpha");

        var two = new Dictionary<ModId, Mod>(installed) { [Id("b.two")] = ModOf("b.two", "Bravo") };
        Plan([Missing("a.one", "x.y"), Missing("b.two", "x.y")], two)[0].RequiredByText
            .Should().Be("Required by Alpha and 1 other");
    }

    /// <summary>Installed-but-inactive is the one case a single click fixes.</summary>
    [Fact]
    public void An_installed_but_inactive_dependency_can_be_activated()
    {
        var installed = new Dictionary<ModId, Mod>
        {
            [Id("a.one")] = ModOf("a.one", "Alpha"),
            [Id("brrainz.harmony")] = ModOf("brrainz.harmony", "Harmony"),
        };

        var card = Plan([Missing("a.one", "brrainz.harmony")], installed, active: [Id("a.one")])[0];

        card.State.Should().Be(DependencyState.InstalledButInactive);
        card.CanActivate.Should().BeTrue();
        card.Unactionable.Should().BeNull();
    }

    [Fact]
    public void A_dependency_that_is_not_on_disk_offers_a_download_when_it_has_a_workshop_id()
    {
        var declared = new Dictionary<ModId, ModDependency>
        {
            [Id("x.y")] = new(Id("x.y"), "Some Mod",
                SteamWorkshopUrl: "https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077"),
        };

        var card = Plan([Missing("a.one", "x.y")], declared: declared)[0];

        card.State.Should().Be(DependencyState.NotInstalled);
        card.WorkshopId.Should().Be("2009463077");
        card.CanDownload.Should().BeTrue();
        card.DisplayName.Should().Be("Some Mod", "the requiring mod declared a friendlier name");
    }

    /// <summary>
    /// With no link there is nowhere to send anyone. Saying so beats a Download button
    /// that cannot know what to fetch.
    /// </summary>
    [Fact]
    public void A_dependency_with_no_workshop_link_says_so_instead_of_offering_a_download()
    {
        var card = Plan([Missing("a.one", "mystery.mod")])[0];

        card.CanDownload.Should().BeFalse();
        card.CanOpenWorkshop.Should().BeFalse();
        card.Unactionable.Should().Contain("No Workshop link");
    }

    /// <summary>
    /// DLC is bought from Steam and enabled by RimWorld. Both facts get said rather than
    /// offering buttons that cannot work.
    /// </summary>
    [Fact]
    public void Dlc_is_reported_honestly_rather_than_offered_as_an_action()
    {
        var notOwned = Plan([MissingDlc("a.one", "ludeon.rimworld.royalty")])[0];
        notOwned.State.Should().Be(DependencyState.DlcNotOwned);
        notOwned.CanActivate.Should().BeFalse();
        notOwned.CanDownload.Should().BeFalse();
        notOwned.Unactionable.Should().Contain("cannot buy DLC");

        var owned = Plan([MissingDlc("a.one", "ludeon.rimworld.royalty")],
            owned: [Id("ludeon.rimworld.royalty")])[0];
        owned.State.Should().Be(DependencyState.DlcNotActive);
        owned.Unactionable.Should().Contain("RimWorld's own mod list");
    }

    /// <summary>
    /// Actionable first. A list opening with three DLC you do not own reads as "nothing
    /// can be done here" and gets closed.
    /// </summary>
    [Fact]
    public void Cards_you_can_act_on_come_first()
    {
        var installed = new Dictionary<ModId, Mod>
        {
            [Id("a.one")] = ModOf("a.one", "Alpha"),
            [Id("z.installed")] = ModOf("z.installed", "Zeta"),
        };

        var cards = Plan(
            [MissingDlc("a.one", "ludeon.rimworld.royalty"),
             Missing("a.one", "z.installed")],
            installed, active: [Id("a.one")]);

        cards[0].CanActivate.Should().BeTrue("the fixable one leads");
        cards[^1].State.Should().Be(DependencyState.DlcNotOwned);
    }

    [Fact]
    public void Issues_that_are_not_about_dependencies_are_ignored()
    {
        var noise = new ValidationIssue(
            ValidationSeverity.Warning, IssueCodes.IncompatibleActive, "x", Id("a"), Id("b"));

        Plan([noise]).Should().BeEmpty();
    }

    [Fact]
    public void The_summary_says_how_much_can_actually_be_fixed()
    {
        var installed = new Dictionary<ModId, Mod>
        {
            [Id("a.one")] = ModOf("a.one", "Alpha"),
            [Id("z.installed")] = ModOf("z.installed", "Zeta"),
        };

        var cards = Plan(
            [Missing("a.one", "z.installed"), MissingDlc("a.one", "ludeon.rimworld.royalty")],
            installed, active: [Id("a.one")]);

        var summary = DependencyResolver.Summary(cards);

        summary.Should().StartWith("2 unmet");
        summary.Should().Contain("1 can be activated").And.Contain("1 need you");

        DependencyResolver.Summary([]).Should().Be("Nothing to resolve.");
    }
}
