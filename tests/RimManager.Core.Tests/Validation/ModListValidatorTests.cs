using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;
using RimManager.Core.Validation;
using Xunit;
using static RimManager.Core.Tests.Sorting.SortFixtures;

namespace RimManager.Core.Tests.Validation;

public sealed class ModListValidatorTests
{
    private static readonly ModListValidator Validator = new();
    private static readonly ModId[] NoExpansions = [];

    /// <summary>A community database whose only entry forces one mod to the very top.</summary>
    private static LoadOrderRules LoadTop(string id) =>
        new(ImmutableDictionary<ModId, ModRules>.Empty.Add(
            ModId.From(id), new ModRules { LoadTop = true }));

    [Fact]
    public void Flags_a_missing_dependency()
    {
        var active = new[] { Mod("a.mod", dependencies: ["needed.lib"]) };

        var report = Validator.Validate(active, NoExpansions, "1.6");

        report.Issues.Should().ContainSingle(i => i.Code == IssueCodes.MissingDependency
            && i.Related == ModId.From("needed.lib"));
    }

    [Fact]
    public void Satisfied_dependency_is_clean()
    {
        var active = new[] { Mod("a.mod", dependencies: ["needed.lib"]), Mod("needed.lib") };

        Validator.Validate(active, NoExpansions, "1.6").Issues
            .Should().NotContain(i => i.Code == IssueCodes.MissingDependency);
    }

    /// <summary>
    /// "Installed and one click away" and "not on this machine" are different problems
    /// with different fixes — dep.inactive was declared for exactly this and then
    /// never emitted, so every unmet dependency sent the user to the Workshop for
    /// mods already sitting in the inactive pane.
    /// </summary>
    [Fact]
    public void An_installed_but_inactive_dependency_gets_its_own_code_and_words()
    {
        var active = new[] { Mod("a.mod", dependencies: ["needed.lib"]) };
        var inactive = new[] { Mod("needed.lib") };

        var report = Validator.Validate(
            active, NoExpansions, "1.6", community: null, inactive: inactive);

        var issue = report.Issues.Should().ContainSingle(
            i => i.Code == IssueCodes.DependencyInactive).Which;
        issue.Related.Should().Be(ModId.From("needed.lib"));
        issue.Message.Should().Contain("installed but not active");
        report.Issues.Should().NotContain(i => i.Code == IssueCodes.MissingDependency,
            "one unmet dependency is one issue, under whichever code tells the truth");
    }

    [Fact]
    public void Flags_a_missing_dlc_and_notes_ownership()
    {
        var active = new[] { Mod("a.mod", dependencies: ["ludeon.rimworld.royalty"]) };

        var owned = Validator.Validate(active, [ModId.From("ludeon.rimworld.royalty")], "1.6");
        owned.Issues.Should().ContainSingle(i => i.Code == IssueCodes.MissingDlc && i.Message.Contains("owned"));

        var notOwned = Validator.Validate(active, NoExpansions, "1.6");
        notOwned.Issues.Should().ContainSingle(i => i.Code == IssueCodes.MissingDlc && i.Message.Contains("not owned"));
    }

    [Fact]
    public void Flags_active_incompatibility_once()
    {
        var active = new[]
        {
            Mod("a.one", incompatibleWith: ["a.two"]),
            Mod("a.two", incompatibleWith: ["a.one"]),
        };

        Validator.Validate(active, NoExpansions, "1.6").Issues
            .Should().ContainSingle(i => i.Code == IssueCodes.IncompatibleActive);
    }

    [Fact]
    public void Flags_a_violated_load_order_rule()
    {
        // a.mod says it loads before b.mod, but the current order is [b, a].
        var active = new[] { Mod("b.mod"), Mod("a.mod", loadBefore: ["b.mod"]) };

        Validator.Validate(active, NoExpansions, "1.6").Issues
            .Should().ContainSingle(i => i.Code == IssueCodes.OrderViolated);
    }

    [Fact]
    public void Correct_order_has_no_violation()
    {
        var active = new[] { Mod("a.mod", loadBefore: ["b.mod"]), Mod("b.mod") };

        Validator.Validate(active, NoExpansions, "1.6").Issues
            .Should().NotContain(i => i.Code == IssueCodes.OrderViolated);
    }

    /// <summary>
    /// The validator sees the same effective rules the sorter does. A community rule
    /// the user disabled must stop warning — a warning about a rule you switched off
    /// is the editor telling you it ignored you.
    /// </summary>
    [Fact]
    public void A_disabled_rule_stops_warning()
    {
        var active = new[] { Mod("b.mod"), Mod("a.mod") };
        var community = new LoadOrderRules(
            new Dictionary<ModId, ModRules>
            {
                [ModId.From("a.mod")] = new ModRules { LoadBefore = [new RuleRef(ModId.From("b.mod"))] },
            }.ToImmutableDictionary());

        Validator.Validate(active, NoExpansions, "1.6", community).Issues
            .Should().ContainSingle(i => i.Code == IssueCodes.OrderViolated,
                "sanity: the rule warns while it is on");

        var overrides = RuleOverrides.Empty
            .Disable(ModId.From("a.mod"), ModId.From("b.mod"));

        Validator.Validate(active, NoExpansions, "1.6", community, overrides: overrides).Issues
            .Should().NotContain(i => i.Code == IssueCodes.OrderViolated);
    }

    /// <summary>A rule the user wrote warns when the order violates it.</summary>
    [Fact]
    public void A_violated_user_rule_warns()
    {
        var active = new[] { Mod("b.mod"), Mod("a.mod") };
        var overrides = RuleOverrides.Empty
            .WithUserRule(new UserRule(ModId.From("a.mod"), ModId.From("b.mod")));

        Validator.Validate(active, NoExpansions, "1.6", overrides: overrides).Issues
            .Should().ContainSingle(i => i.Code == IssueCodes.OrderViolated,
                "a rule you wrote yourself is the one you most expect the panel to hold you to");
    }

    [Fact]
    public void Flags_unsupported_game_version_but_skips_core_and_dlc()
    {
        var active = new[]
        {
            Mod("old.mod", supportedVersions: ["1.4", "1.5"]),
            Mod("cur.mod", supportedVersions: ["1.6"]),
            Mod("ludeon.rimworld", ModSource.Core, supportedVersions: ["1.5"]),
        };

        var report = Validator.Validate(active, NoExpansions, "1.6");

        report.Issues.Should().ContainSingle(i => i.Code == IssueCodes.UnsupportedVersion
            && i.Subject == ModId.From("old.mod"));
        report.Issues.Should().NotContain(i => i.Subject == ModId.From("ludeon.rimworld"));
    }

    [Fact]
    public void A_clean_list_reports_nothing()
    {
        var active = new[]
        {
            Mod("lib", supportedVersions: ["1.6"]),
            Mod("consumer", loadAfter: ["lib"], dependencies: ["lib"], supportedVersions: ["1.6"]),
        };

        Validator.Validate(active, NoExpansions, "1.6").IsClean.Should().BeTrue();
    }

    /// <summary>
    /// The pre-patcher chain, end to end, from nothing but what the mods declare.
    /// <para>
    /// On a real install Prepatcher, Loading Progress and Better Stacktraces all sat
    /// below Core with their own <c>loadBefore</c> declarations silently discarded —
    /// the sorter dropped them as tier violations and the validator, which reported
    /// only APPLIED edges, never mentioned them. No community rule was involved and
    /// none was added to fix it.
    /// </para>
    /// </summary>
    [Fact]
    public void A_pre_patcher_below_core_is_reported_rather_than_silently_dropped()
    {
        var mods = new[]
        {
            Mod("ludeon.rimworld", ModSource.Core),
            Mod("brrainz.harmony"),
            Mod("zetrith.prepatcher", loadBefore: ["ludeon.rimworld", "brrainz.harmony"]),
        };

        var report = Validator.Validate(mods, NoExpansions, "1.6");

        report.Issues.Where(i => i.Code == IssueCodes.OrderViolated)
            .Should().HaveCount(2, "prepatcher is after both mods it declares it precedes");
        report.Issues.Should().Contain(i =>
            i.Code == IssueCodes.OrderViolated && i.Subject == ModId.From("zetrith.prepatcher"));
    }

    /// <summary>
    /// Sorting fixes it — which is the proof the rule is now genuinely applied rather
    /// than merely reported. A warning you cannot clear is worse than none.
    /// </summary>
    [Fact]
    public void Sorting_puts_the_pre_patcher_where_its_declaration_asks()
    {
        var mods = new[]
        {
            Mod("ludeon.rimworld", ModSource.Core),
            Mod("brrainz.harmony"),
            Mod("zetrith.prepatcher", loadBefore: ["ludeon.rimworld", "brrainz.harmony"]),
            Mod("ilyvion.loadingprogress", loadBefore: ["ludeon.rimworld"],
                loadAfter: ["brrainz.harmony"]),
        };

        var order = new ModSorter().Sort(mods, RuleGraphBuilder.Build(mods, null)).Order;
        var at = order.Select((id, i) => (id, i)).ToDictionary(x => x.id.Value, x => x.i);

        at["zetrith.prepatcher"].Should().BeLessThan(at["brrainz.harmony"]);
        at["brrainz.harmony"].Should().BeLessThan(at["ilyvion.loadingprogress"]);
        at["ilyvion.loadingprogress"].Should().BeLessThan(at["ludeon.rimworld"]);

        var sorted = mods.OrderBy(m => at[m.PackageId.Value]).ToArray();
        Validator.Validate(sorted, NoExpansions, "1.6").Issues
            .Where(i => i.Code == IssueCodes.OrderViolated)
            .Should().BeEmpty("the declarations are all satisfiable and now all applied");
    }

    /// <summary>
    /// A rule that tiering really does overrule is REPORTED, not discarded. Ten were
    /// being dropped in silence on a real install. Tiering dominating rules is the
    /// design (§4.4); dominating them invisibly is how a user concludes the app missed
    /// something.
    /// </summary>
    [Fact]
    public void A_rule_overruled_by_tiering_is_reported_instead_of_vanishing()
    {
        // The real residue, from the developer's own install: the community database
        // marks XML Extensions loadTop AND says Winston Waves must load BEFORE it. An
        // ordinary mod cannot precede a Top mod, so no ordering satisfies both — a
        // contradiction inside the database itself, not an artefact of tiering.
        var mods = new[]
        {
            Mod("imranfish.xmlextensions"),
            Mod("vsew.winstonwave", loadBefore: ["imranfish.xmlextensions"]),
        };

        var report = Validator.Validate(mods, NoExpansions, "1.6", LoadTop("imranfish.xmlextensions"));

        report.Issues.Should().Contain(i => i.Code == IssueCodes.OrderTierOverride,
            "the rule cannot be honoured, and saying nothing is how it vanished before");
    }

    /// <summary>
    /// The wording attributes nothing. "X declares it loads before Y" was wrong half
    /// the time: a <c>loadAfter</c> on Y produces the edge X -> Y, so the sentence
    /// credited the declaration to the mod that had not made it — on a real install,
    /// to the base game itself.
    /// </summary>
    [Fact]
    public void A_tier_override_never_claims_who_declared_the_rule()
    {
        var mods = new[]
        {
            Mod("imranfish.xmlextensions"),
            Mod("vsew.winstonwave", loadBefore: ["imranfish.xmlextensions"]),
        };

        var report = Validator.Validate(mods, NoExpansions, "1.6", LoadTop("imranfish.xmlextensions"));

        var issue = report.Issues.Single(i => i.Code == IssueCodes.OrderTierOverride);

        // The rule, and nothing else. It named its own source and then explained our
        // tier mechanism to somebody who only wanted to know whether one mod loads
        // before another.
        issue.Message.Should().Be("'vsew.winstonwave' should load before 'imranfish.xmlextensions'.");
        issue.Message.Should().NotContain("declares");
        issue.Message.Should().NotContain("tier");
        issue.Message.Should().NotContain("Community");
    }

    /// <summary>
    /// An order issue records WHO WROTE THE RULE, which is frequently not its subject.
    /// <para>
    /// A <c>loadAfter</c> on B produces the edge A → B, so the subject is A — the mod
    /// that declared nothing. Measured on a real install, attributing by subject hung
    /// four warnings on the base game, which declares no rules whatsoever, while the
    /// mods that actually wrote them showed clean.
    /// </para>
    /// </summary>
    [Fact]
    public void An_order_issue_records_the_mod_that_declared_the_rule()
    {
        var mods = new[]
        {
            // xmlextensions says "load me after Core", producing core -> xmlextensions.
            Mod("imranfish.xmlextensions", loadAfter: ["ludeon.rimworld"]),
            Mod("ludeon.rimworld", ModSource.Core),
        };

        var issue = Validator.Validate(mods, NoExpansions, "1.6").Issues
            .Single(i => i.Code == IssueCodes.OrderViolated);

        issue.Subject.Should().Be(ModId.From("ludeon.rimworld"), "the edge starts at Core");
        issue.DeclaredBy.Should().Be(ModId.From("imranfish.xmlextensions"), "but Core wrote nothing");
        issue.Owner.Should().Be(ModId.From("imranfish.xmlextensions"),
            "the warning belongs to the mod whose rule is unmet");
    }

    /// <summary>A tier override records its author for the same reason.</summary>
    [Fact]
    public void A_tier_override_also_records_the_mod_that_declared_the_rule()
    {
        var mods = new[]
        {
            Mod("imranfish.xmlextensions"),
            Mod("vsew.winstonwave", loadBefore: ["imranfish.xmlextensions"]),
        };

        var issue = Validator.Validate(mods, NoExpansions, "1.6", LoadTop("imranfish.xmlextensions"))
            .Issues.Single(i => i.Code == IssueCodes.OrderTierOverride);

        issue.Owner.Should().Be(ModId.From("vsew.winstonwave"), "it wrote the rule");
    }

    /// <summary>
    /// Findings that ARE about their subject leave the declarer unset, so Owner falls
    /// through to the subject: a mod's own missing dependency, its own declared
    /// incompatibility, its own unsupported version.
    /// </summary>
    [Fact]
    public void Findings_about_the_subject_itself_need_no_declarer()
    {
        var mods = new[]
        {
            Mod("a.mod", dependencies: ["missing.dep"], incompatibleWith: ["b.mod"],
                supportedVersions: ["1.4"]),
            Mod("b.mod", supportedVersions: ["1.6"]),
        };

        var report = Validator.Validate(mods, NoExpansions, "1.6");

        report.Issues.Where(i => i.Code != IssueCodes.OrderViolated
                              && i.Code != IssueCodes.OrderTierOverride)
            .Should().OnlyContain(i => i.Owner == i.Subject)
            .And.Contain(i => i.Code == IssueCodes.MissingDependency)
            .And.Contain(i => i.Code == IssueCodes.IncompatibleActive)
            .And.Contain(i => i.Code == IssueCodes.UnsupportedVersion);

        // The mod that DECLARED the incompatibility owns it; the one it names does not.
        report.Issues.Single(i => i.Code == IssueCodes.IncompatibleActive)
            .Owner.Should().Be(ModId.From("a.mod"));
    }

    // --- intrinsic vs relational (N2 - UI-7.2) --------------------------------
    //
    // Relational checks are about the LIST; intrinsic checks are about the MOD. That
    // line decides which mods each check may be asked about, and it is the whole of
    // what an inactive row may carry.

    /// <summary>
    /// An unsupported version is a fact about the mod, true whether or not you load it
    /// — and it is exactly what you want to know while deciding whether to.
    /// </summary>
    [Fact]
    public void An_inactive_mod_is_still_checked_against_the_game_version()
    {
        var inactive = new[] { Mod("old.mod", supportedVersions: ["1.4", "1.5"]) };

        var report = Validator.Validate([], NoExpansions, "1.6", community: null, inactive: inactive);

        report.Issues.Should().ContainSingle()
            .Which.Should().Match<ValidationIssue>(i =>
                i.Code == IssueCodes.UnsupportedVersion
                && i.Owner == ModId.From("old.mod"));
    }

    /// <summary>
    /// An inactive mod's dependency is not missing — nothing is asking for it. Reporting
    /// it would put a blocking glyph on a mod that breaks nothing, which is the loudest
    /// possible way to be wrong.
    /// </summary>
    [Fact]
    public void An_inactive_mods_dependency_is_not_missing()
    {
        var inactive = new[] { Mod("needy.mod", dependencies: ["absent.dep"]) };

        Validator.Validate([], NoExpansions, "1.6", community: null, inactive: inactive)
            .Issues.Should().NotContain(i => i.Code == IssueCodes.MissingDependency);
    }

    /// <summary>
    /// Two mods that declare each other incompatible are not in conflict while one of
    /// them is not loaded. That is the entire point of deactivating one of them.
    /// </summary>
    [Fact]
    public void An_incompatibility_with_an_inactive_mod_is_not_reported()
    {
        var active = new[] { Mod("a.mod", incompatibleWith: ["b.mod"]) };
        var inactive = new[] { Mod("b.mod") };

        Validator.Validate(active, NoExpansions, "1.6", community: null, inactive: inactive)
            .Issues.Should().NotContain(i => i.Code == IssueCodes.IncompatibleActive);
    }

    /// <summary>
    /// An inactive mod has no position, so no load-order rule about it can be violated
    /// — including one it declares itself.
    /// </summary>
    [Fact]
    public void An_inactive_mod_produces_no_load_order_warnings()
    {
        var active = new[] { Mod("ludeon.rimworld", ModSource.Core) };
        var inactive = new[] { Mod("late.mod", loadBefore: ["ludeon.rimworld"]) };

        var report = Validator.Validate(active, NoExpansions, "1.6", community: null, inactive: inactive);

        report.Issues.Should().NotContain(i =>
            i.Code == IssueCodes.OrderViolated || i.Code == IssueCodes.OrderTierOverride);
    }

    /// <summary>
    /// The whole rule in one assertion: every issue an inactive mod owns is intrinsic.
    /// Stated as an allow-list of codes rather than a deny-list, so a check added later
    /// has to decide which kind it is instead of leaking to the inactive pane by
    /// default. N7's <see cref="IssueCodes.ReplacementAvailable"/> chose intrinsic and
    /// joined the list.
    /// </summary>
    [Fact]
    public void Everything_an_inactive_mod_owns_is_intrinsic()
    {
        var active = new[]
        {
            Mod("ludeon.rimworld", ModSource.Core),
            Mod("a.mod", incompatibleWith: ["shelved.mod"], supportedVersions: ["1.6"]),
        };
        var inactive = new[]
        {
            Mod("shelved.mod", dependencies: ["absent.dep"], loadBefore: ["ludeon.rimworld"],
                incompatibleWith: ["a.mod"], supportedVersions: ["1.4"]),
        };
        var replacements = ImmutableArray.Create(new Core.ModDatabases.ModReplacement(
            "1", ModId.From("shelved.mod"), "Shelved",
            "2", ModId.From("shelved.continued"), "Shelved (Continued)", "Mlie", ["1.6"]));

        var report = Validator.Validate(
            active, NoExpansions, "1.6", community: null, inactive: inactive,
            knownGood: null, replacements: replacements);

        var inactiveIds = inactive.Select(m => m.PackageId).ToHashSet();
        var theirs = report.Issues.Where(i => i.Owner is { } o && inactiveIds.Contains(o)).ToList();

        theirs.Should().NotBeEmpty("the shelved mod does not support 1.6 and has a replacement");
        theirs.Should().OnlyContain(
            i => i.Code == IssueCodes.UnsupportedVersion || i.Code == IssueCodes.ReplacementAvailable,
            "only checks about the MOD may reach a mod that is not loaded");
        theirs.Should().Contain(i => i.Code == IssueCodes.ReplacementAvailable,
            "the intrinsic replacement check reaches inactive mods — deciding whether "
            + "to load a mod is exactly when a replacement is worth knowing about");
    }

    // --- N7 · the Mlie databases ---------------------------------------------

    /// <summary>
    /// NoVersionWarning suppresses the unsupported-version warning for listed mods —
    /// and only for them. The list is per game version, fetched for the running one.
    /// </summary>
    [Fact]
    public void A_known_good_mod_loses_its_version_warning_and_nobody_else_does()
    {
        var active = new[]
        {
            Mod("reported.working", supportedVersions: ["1.5"]),
            Mod("actually.stale", supportedVersions: ["1.5"]),
        };
        var knownGood = new Core.ModDatabases.KnownGoodDatabase(
            [ModId.From("reported.working")], "<ModIdsToFix/>");

        var report = Validator.Validate(
            active, NoExpansions, "1.6", community: null, inactive: null, knownGood: knownGood);

        report.Issues.Should().ContainSingle(i => i.Code == IssueCodes.UnsupportedVersion)
            .Which.Subject.Should().Be(ModId.From("actually.stale"));
    }

    [Fact]
    public void A_replacement_produces_the_intrinsic_finding_with_the_new_mods_name()
    {
        var active = new[] { Mod("old.mod") };
        var replacements = ImmutableArray.Create(new Core.ModDatabases.ModReplacement(
            "1", ModId.From("old.mod"), "Old",
            "2", ModId.From("new.mod"), "Old (Continued)", "Mlie", ["1.6"]));

        var report = Validator.Validate(
            active, NoExpansions, "1.6", community: null, inactive: null,
            knownGood: null, replacements: replacements);

        report.Issues.Should().ContainSingle(i => i.Code == IssueCodes.ReplacementAvailable)
            .Which.Message.Should().Contain("Old (Continued)").And.Contain("Mlie");
    }

    /// <summary>Without the databases nothing changes — both parameters default off.</summary>
    [Fact]
    public void Absent_databases_leave_the_report_exactly_as_before()
    {
        var active = new[] { Mod("plain.mod", supportedVersions: ["1.5"]) };

        var report = Validator.Validate(active, NoExpansions, "1.6");

        report.Issues.Should().ContainSingle(i => i.Code == IssueCodes.UnsupportedVersion);
        report.Issues.Should().NotContain(i => i.Code == IssueCodes.ReplacementAvailable);
    }

    /// <summary>
    /// The fix, stated as the case it removes. RimSort's database marks XML Extensions
    /// <c>loadTop</c> and, on the very same entry, says it must load after every DLC —
    /// commented "Should Always load after DLC". Both hold, because loadTop means top of
    /// the MODS. Reading it as top of the FILE made five ordinary rules into tier
    /// violations and reported them as overridden.
    /// </summary>
    [Fact]
    public void A_load_top_framework_may_still_be_required_after_the_dlc()
    {
        var mods = new[]
        {
            Mod("ludeon.rimworld", ModSource.Core),
            Mod("ludeon.rimworld.royalty", ModSource.Dlc),
            Mod("imranfish.xmlextensions",
                loadAfter: ["ludeon.rimworld", "ludeon.rimworld.royalty"]),
        };

        var report = Validator.Validate(mods, NoExpansions, "1.6", LoadTop("imranfish.xmlextensions"));

        report.Issues.Should().NotContain(i => i.Code == IssueCodes.OrderTierOverride,
            "a loadTop framework loading after the DLC is exactly what the database asks for");
        report.Issues.Should().NotContain(i => i.Code == IssueCodes.OrderViolated,
            "and the order already satisfies it");
    }

    /// <summary>
    /// A rule the tiers overrule is reported only when the ORDER actually breaks it.
    /// It used to be reported unconditionally, which produced a warning the user could
    /// not clear by any means — including dragging the two mods into exactly the order
    /// the rule asks for. "A should load before B" while A does load before B is simply
    /// false, whatever the sorter would do on its next run.
    /// </summary>
    [Fact]
    public void A_tier_overruled_rule_clears_when_the_order_satisfies_it_by_hand()
    {
        var rules = LoadTop("imranfish.xmlextensions");

        // Sorter's own order: the Top framework first, so the rule is broken.
        var broken = new[]
        {
            Mod("imranfish.xmlextensions"),
            Mod("vsew.winstonwave", loadBefore: ["imranfish.xmlextensions"]),
        };
        Validator.Validate(broken, NoExpansions, "1.6", rules).Issues
            .Should().Contain(i => i.Code == IssueCodes.OrderTierOverride);

        // Dragged by hand into the order the rule wants: nothing left to say.
        var byHand = new[]
        {
            Mod("vsew.winstonwave", loadBefore: ["imranfish.xmlextensions"]),
            Mod("imranfish.xmlextensions"),
        };
        Validator.Validate(byHand, NoExpansions, "1.6", rules).Issues
            .Should().NotContain(i => i.Code == IssueCodes.OrderTierOverride,
                "the order satisfies the rule, so there is nothing to warn about");
    }

    /// <summary>
    /// NF-10 · a recognized mod-list item is not a mod, and the intrinsic checks skip
    /// it: "unsupported version" would sit amber forever on something the game never
    /// loads, which is how a warning surface trains people to ignore it.
    /// </summary>
    [Fact]
    public void A_list_item_is_exempt_from_the_intrinsic_checks()
    {
        var listItem = new Mod
        {
            PackageId = ModId.From("author.somelist"),
            Name = "Some List",
            Source = ModSource.Workshop,
            RootPath = "/ws/123",
            Content = ContentFlags.RwList,
            SupportedVersions = ["1.4"], // stale on purpose: the exemption is the test
        };

        var report = Validator.Validate(
            [], NoExpansions, "1.6", community: null, inactive: [listItem]);

        report.Issues.Should().NotContain(i => i.Subject == listItem.PackageId,
            "a list item has no version to support and nothing to replace");
    }
}
