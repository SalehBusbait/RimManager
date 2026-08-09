using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The load state names what it is doing, and counts it.
/// <para>
/// The title used to be hardcoded in the card's markup, so a modlist switch — which spends
/// most of its time copying <c>Mod_*.xml</c> files in and out of the game's config folder,
/// a few hundred of them on a real install — displayed "Reading mod folders…" while doing
/// nothing of the sort. The one phase the fixed title was wrong for was the one the user
/// was waiting on.
/// </para>
/// </summary>
public sealed class LoadPhaseTextTests
{
    private static LoadPhase[] All => Enum.GetValues<LoadPhase>();

    [Fact]
    public void Every_phase_says_something_and_says_it_differently()
    {
        foreach (var phase in All) LoadPhaseText.For(phase).Should().NotBeNullOrWhiteSpace();

        All.Select(LoadPhaseText.For).Distinct().Should().HaveCount(All.Length,
            "a phase that reads the same as another is a phase the user cannot tell apart");
    }

    /// <summary>
    /// Every one is present continuous. The card is up because something is happening, and a
    /// title in any other tense reads as a result rather than as work in progress.
    /// </summary>
    [Fact]
    public void Every_phase_reads_as_work_in_progress()
    {
        foreach (var phase in All)
            LoadPhaseText.For(phase).Should().EndWith("…", $"{phase} is a thing being done");
    }

    /// <summary>
    /// Every phase names what its numbers count, so the line under the bar reads as a
    /// sentence — "214 / 292 mods" — rather than as two bare numbers whose meaning changes
    /// silently between phases.
    /// </summary>
    [Fact]
    public void Every_phase_names_what_it_is_counting()
    {
        foreach (var phase in All)
        {
            LoadPhaseText.Unit(phase).Should().NotBeNullOrWhiteSpace();
            LoadPhaseText.Unit(phase).Should().NotContain("…", "a unit is a noun, not a state");
        }
    }

    /// <summary>
    /// The two file-copy phases count the same thing, and the two that walk mod folders do
    /// not — the unit follows the work rather than the phase name.
    /// </summary>
    [Fact]
    public void The_unit_follows_the_work()
    {
        LoadPhaseText.Unit(LoadPhase.SavingModSettings)
            .Should().Be(LoadPhaseText.Unit(LoadPhase.RestoringModSettings));

        LoadPhaseText.Unit(LoadPhase.ReadingMods)
            .Should().NotBe(LoadPhaseText.Unit(LoadPhase.SavingModSettings));
    }

    /// <summary>
    /// The default phase is the scan, because that is what a plain reload does — and a
    /// default of "restoring mod settings" would mislabel every startup.
    /// </summary>
    [Fact]
    public void The_default_phase_is_the_scan()
    {
        default(LoadPhase).Should().Be(LoadPhase.ReadingMods);
    }

    /// <summary>
    /// The card must not hardcode a title again. This is the regression that prompted the
    /// type, and markup is where it would come back.
    /// </summary>
    [Fact]
    public void The_card_binds_its_title_rather_than_stating_one()
    {
        Card.Should().Contain("{Binding LoadPhaseLabel}");
        Card.Should().NotContain("Text=\"Reading mod folders",
            "the title comes from the phase, not from the markup");
    }

    /// <summary>
    /// And it no longer offers a way out. The escape hatch existed because the conflict phase
    /// could only show a moving stripe; now that every phase reports a real fraction, a bar
    /// visibly moving through 214 / 292 answers the same worry better than a button.
    /// </summary>
    [Fact]
    public void The_card_offers_no_escape_from_a_measurable_wait()
    {
        Card.Should().NotContain("SkipLoadPhase");
        Card.Should().NotContain("Show the list now");
    }

    private static string Card => File.ReadAllText(
        Path.Combine(RepoPaths.AppProject, "Views", "Shell", "ScanStateView.axaml"));
}
