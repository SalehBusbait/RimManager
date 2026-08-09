using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Validation;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// "Click the row's glyph and land on the warning it stands for" (N2 · UI-4), and the
/// completeness the toolbar chip depends on (UI-7.1).
/// </summary>
public sealed class WarningsSelectForTests
{
    private static readonly ModId Subject = ModId.From("a.subject");
    private static readonly ModId Related = ModId.From("b.related");
    private static readonly ModId Stranger = ModId.From("c.stranger");

    private static WarningsViewModel Panel(params ValidationIssue[] issues)
    {
        var vm = new WarningsViewModel();
        vm.Populate(issues, [], _ => null, new Dictionary<ModId, string>(), "test");
        return vm;
    }

    private static ValidationIssue Order() => new(
        ValidationSeverity.Warning, IssueCodes.OrderViolated,
        $"'{Subject.Display}' should load before '{Related.Display}' (About/LoadAfter) but currently loads after it.",
        Subject, Related, DeclaredBy: Subject);

    private static ValidationIssue Incompatible() => new(
        ValidationSeverity.Error, IssueCodes.IncompatibleActive,
        $"'{Subject.Display}' is incompatible with '{Related.Display}', but both are active.",
        Subject, Related, DeclaredBy: Subject);

    /// <summary>
    /// A warning belongs to the mod that DECLARED it, and to no one else. A mod is not
    /// at fault for being referred to: TakeCover declared the incompatibility, so
    /// Achtung! — which declared nothing — does not carry it.
    /// </summary>
    [Fact]
    public void A_warning_belongs_to_the_mod_that_declared_it()
    {
        var panel = Panel(Incompatible());

        panel.For(Subject).Should().ContainSingle();
        panel.For(Related).Should().BeEmpty("the referred mod wrote no rule");
        panel.For(Stranger).Should().BeEmpty();
    }

    /// <summary>
    /// The case that made Subject alone insufficient, measured on a real install.
    /// <para>
    /// An edge is built from whichever mod wrote the rule, so XmlExtensions declaring
    /// <c>loadAfter Ludeon.RimWorld</c> produces the edge <c>rimworld → xmlextensions</c>
    /// whose SUBJECT is the base game. Attributing by subject hung the warning on
    /// RimWorld — a mod that declares nothing whatsoever — while the mod that actually
    /// wrote the rule showed clean.
    /// </para>
    /// </summary>
    [Fact]
    public void An_order_rule_belongs_to_its_author_not_to_the_mod_it_points_at()
    {
        var declarer = ModId.From("imranfish.xmlextensions");
        var core = ModId.From("Ludeon.RimWorld");

        var panel = Panel(new ValidationIssue(
            ValidationSeverity.Warning, IssueCodes.OrderViolated,
            $"'{core.Display}' should load before '{declarer.Display}' (About/LoadAfter) but currently loads after it.",
            Subject: core, Related: declarer, DeclaredBy: declarer));

        panel.For(declarer).Should().ContainSingle("its own loadAfter produced this edge");
        panel.For(core).Should().BeEmpty("the base game declares nothing at all");
    }

    /// <summary>
    /// With no declarer recorded the subject owns it — dependencies, incompatibilities
    /// and version warnings are all about the mod that stated the requirement.
    /// </summary>
    [Fact]
    public void Without_a_declarer_the_subject_owns_the_warning()
    {
        var panel = Panel(new ValidationIssue(
            ValidationSeverity.Warning, IssueCodes.UnsupportedVersion,
            $"'{Subject.Display}' does not list support for 1.6.", Subject));

        panel.For(Subject).Should().ContainSingle();
    }

    [Fact]
    public void Selecting_for_a_mod_picks_the_warning_that_names_it()
    {
        var panel = Panel(Order());

        panel.SelectFor(Subject).Should().BeTrue();
        panel.Selected.Should().NotBeNull();
        panel.Selected!.IsGroupHeader.Should().BeFalse();
        panel.Selected.Subject.Should().Be(Subject);
    }

    /// <summary>
    /// Nothing in the dock names this mod, so nothing moves. A row carries this glyph
    /// for an update and for a dirty working tree too, and jumping to a tab with no
    /// mention of the mod is worse than not moving at all.
    /// </summary>
    [Fact]
    public void Selecting_for_an_unmentioned_mod_changes_nothing()
    {
        var panel = Panel(Order());

        panel.SelectFor(Stranger).Should().BeFalse();
        panel.Selected.Should().BeNull();
    }

    /// <summary>
    /// The chips are a lens, so following a link through one has to widen it. With the
    /// chip left on Blocking, selecting a Warning-tone row would pick something not in
    /// the table, and the dock would look like it had ignored the click.
    /// </summary>
    [Fact]
    public void A_severity_chip_that_would_hide_the_warning_is_cleared_first()
    {
        var panel = Panel(Order());
        panel.ShowBlocking = true;

        panel.Rows.Should().NotContain(r => !r.IsGroupHeader,
            "the test is meaningless unless the chip really is hiding it");

        panel.SelectFor(Subject).Should().BeTrue();

        panel.ShowAll.Should().BeTrue();
        panel.Selected.Should().NotBeNull();
        panel.Rows.Should().Contain(panel.Selected!,
            "the selected row has to be one the table is actually showing");
    }

    /// <summary>
    /// A chip that already shows the warning is left alone — widening it would throw
    /// away a filter the user set, for no reason.
    /// </summary>
    [Fact]
    public void A_chip_that_already_shows_the_warning_is_left_alone()
    {
        var panel = Panel(Incompatible());
        panel.ShowBlocking = true;

        panel.SelectFor(Subject).Should().BeTrue();

        panel.ShowBlocking.Should().BeTrue();
        panel.ShowAll.Should().BeFalse();
    }

    /// <summary>Group headers are a rendering device and are never a destination.</summary>
    [Fact]
    public void A_group_header_is_never_selected()
    {
        var panel = Panel(Order(), Incompatible());

        panel.All.Should().OnlyContain(e => !e.IsGroupHeader,
            "All is the unfiltered issue set — headers are added by grouping");

        panel.SelectFor(Subject);
        panel.Selected!.IsGroupHeader.Should().BeFalse();
    }

    /// <summary>
    /// <c>All</c> is what the toolbar chip counts, and it must include every kind — not
    /// just validation issues. The chip used to read the report alone, so a duplicate
    /// packageId was counted in the dock and unreachable from the chip that exists to
    /// find mods with warnings.
    /// </summary>
    [Fact]
    public void All_includes_the_scans_duplicates_not_only_validation_issues()
    {
        var vm = new WarningsViewModel();
        vm.Populate(
            [Order()],
            [new ModWarning(WarningSeverity.Info, "duplicate.packageId", "two folders", Stranger)],
            _ => null, new Dictionary<ModId, string>(), "test");

        vm.All.Should().HaveCount(2);
        vm.For(Stranger).Should().ContainSingle("the duplicate names it");

        var reachable = vm.All
            .SelectMany(e => new[] { e.Subject, e.Related })
            .Where(id => id.HasValue).Select(id => id!.Value)
            .ToImmutableHashSet();

        reachable.Should().Contain(Stranger);
        reachable.Should().Contain(Subject);
        reachable.Should().Contain(Related);
    }

    /// <summary>
    /// The defect the probe found on the real install, and the reason this method
    /// exists: the stored text has its subject elided for the dock's MOD column, so on
    /// the OTHER mod's row it reads as self-reference. XML Extensions' row said
    /// "Should load before imranfish.xmlextensions" — its own packageId — and Achtung's
    /// said "Is incompatible with brrainz.achtung", which is itself.
    /// </summary>
    [Fact]
    public void On_the_other_mods_row_the_subject_is_put_back()
    {
        var panel = Panel(Incompatible());
        var entry = panel.All.Single();

        // On the subject's own row the sentence stands as written.
        WarningsPresenter.MessageFor(entry, Subject)
            .Should().Be(entry.FullIssue)
            .And.StartWith("Is incompatible");

        // On the related mod's row it has to name who is incompatible with what.
        var onRelated = WarningsPresenter.MessageFor(entry, Related);

        onRelated.Should().StartWith(Subject.Display);
        onRelated.Should().Contain("is incompatible with");
        onRelated.Should().NotStartWith("Is incompatible",
            "a sentence with no subject on the other mod's row reads as self-reference");
    }

    /// <summary>
    /// Putting the subject back is the exact inverse of stripping it, so a round trip
    /// through both has to land on the validator's own sentence.
    /// </summary>
    [Fact]
    public void Restoring_the_subject_inverts_stripping_it()
    {
        var panel = Panel(Order());
        var entry = panel.All.Single();

        // The quotes are gone because SplitMessage lifts the packageId into its own
        // mono run for the table; FullIssue re-joins the three runs without them.
        WarningsPresenter.MessageFor(entry, Related)
            .Should().Be($"{Subject.Display} should load before {Related.Display} "
                         + "(About/LoadAfter) but currently loads after it.");
    }

    /// <summary>A warning with no subject at all is left exactly as it is.</summary>
    [Fact]
    public void A_warning_with_no_subject_is_untouched()
    {
        var vm = new WarningsViewModel();
        vm.Populate([], [new ModWarning(WarningSeverity.Info, "duplicate.packageId", "x", null)],
            _ => null, new Dictionary<ModId, string>(), "test");

        var entry = vm.All.Single();
        WarningsPresenter.MessageFor(entry, Stranger).Should().Be(entry.FullIssue);
    }

    /// <summary>
    /// The validator writes packageIds — right for the CLI and the log, where they are
    /// stable and unambiguous. On screen the user is reading a list of mod NAMES, and
    /// "imranfish.xmlextensions" makes them translate every time.
    /// </summary>
    [Fact]
    public void A_warning_names_the_other_mod_rather_than_its_package_id()
    {
        var vm = new WarningsViewModel();
        vm.Populate([Order()], [], _ => null,
            new Dictionary<ModId, string> { [Related] = "Winston Waves" }, "test");

        var entry = vm.All.Single();

        entry.IssueMono.Should().Be("Winston Waves");
        entry.FullIssue.Should().NotContain(Related.Display);
        entry.EmphasisIsMono.Should().BeFalse("a name is prose, not an identifier");
    }

    /// <summary>
    /// A mod that is not installed has no name to show, and inventing one would be
    /// worse than the identifier — so the packageId stays, in mono.
    /// </summary>
    [Fact]
    public void An_uninstalled_mod_keeps_its_package_id_in_mono()
    {
        var vm = new WarningsViewModel();
        vm.Populate([Order()], [], _ => null, new Dictionary<ModId, string>(), "test");

        var entry = vm.All.Single();

        entry.IssueMono.Should().Be(Related.Display);
        entry.EmphasisIsMono.Should().BeTrue();
    }
}
