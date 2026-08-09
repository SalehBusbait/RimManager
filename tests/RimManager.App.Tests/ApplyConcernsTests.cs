using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// When Apply stops to ask, and when it just writes.
/// <para>
/// The bar used to appear on every apply, asking a question with one answer. A
/// confirmation that always fires teaches the hand to dismiss it, and then it is not a
/// confirmation — it is a second click on Apply wearing a warning's clothes. These pin both
/// halves: what earns a stop, and what deliberately does not.
/// </para>
/// </summary>
public sealed class ApplyConcernsTests
{
    [Fact]
    public void A_routine_apply_says_nothing_and_writes()
    {
        ApplyConcerns.For(DriftKind.InSync, blockingErrors: 0).Should().BeEmpty();
        ApplyConcerns.IsRoutine(DriftKind.InSync, 0).Should().BeTrue();
    }

    /// <summary>
    /// Editing the list and applying it is the ordinary use of this app. Stopping for it
    /// would be stopping for the thing the button is for.
    /// </summary>
    [Fact]
    public void Pending_edits_are_not_a_reason_to_stop()
    {
        ApplyConcerns.IsRoutine(DriftKind.PendingApply, 0).Should().BeTrue();
    }

    /// <summary>
    /// A list that has never been applied is a new list, not a hazard. The write is exactly
    /// what the user is asking for.
    /// </summary>
    [Fact]
    public void A_never_applied_list_is_not_a_reason_to_stop()
    {
        ApplyConcerns.IsRoutine(DriftKind.Unknown, 0).Should().BeTrue();
    }

    /// <summary>
    /// The one case that destroys something: RimWorld wrote that file, and this replaces
    /// it with an order the game has never seen.
    /// </summary>
    [Fact]
    public void The_game_having_changed_underneath_always_stops()
    {
        var reasons = ApplyConcerns.For(DriftKind.ChangedOutsideRimManager, blockingErrors: 0);

        reasons.Should().ContainSingle();
        reasons[0].Should().Contain("changed outside RimManager").And.Contain("replaces");
    }

    /// <summary>
    /// Opting out of being <em>stopped</em> by blocking warnings is not opting out of being
    /// <em>told</em>. The refusal is a separate preference; when it is off, the caller
    /// passes the count through and the bar says so once.
    /// </summary>
    [Fact]
    public void Blocking_errors_are_stated_when_the_refusal_is_off()
    {
        // S-COMMIT's copy: "overridden" names the Advanced preference that let this
        // apply through, and both halves are stated — Apply works, the game may not.
        ApplyConcerns.For(DriftKind.InSync, blockingErrors: 3)
            .Should().ContainSingle().Which.Should()
            .Contain("3 blocking warnings overridden")
            .And.Contain("Apply stays available")
            .And.Contain("may fail to load this order");

        // Singular, not "1 blocking warnings".
        ApplyConcerns.For(DriftKind.InSync, blockingErrors: 1)
            .Should().ContainSingle().Which.Should()
            .Contain("1 blocking warning overridden").And.NotContain("warnings");
    }

    [Fact]
    public void Both_reasons_are_stated_together_rather_than_the_first_winning()
    {
        var reasons = ApplyConcerns.For(DriftKind.ChangedOutsideRimManager, blockingErrors: 2);

        reasons.Should().HaveCount(2);
        ApplyConcerns.Summarise(reasons).Should().Contain(" · ");
    }

    [Fact]
    public void The_headline_counts_what_will_be_written()
    {
        ApplyConcerns.Title(73).Should().Be("Apply 73 mods to the game?");
        ApplyConcerns.Title(1).Should().Be("Apply 1 mod to the game?");
    }

    /// <summary>
    /// Pinned as the decision it is: a real install carries a dozen standing warnings for
    /// months, so if ordinary warnings raised the bar it would raise on every apply and we
    /// would be back where we started.
    /// </summary>
    [Fact]
    public void Ordinary_warnings_are_not_passed_in_at_all()
    {
        // The signature takes blocking errors, never a warning count — the type is the
        // guard. This test exists so that adding one is a deliberate act with a failing
        // test attached, rather than a quiet parameter.
        typeof(ApplyConcerns).GetMethod(nameof(ApplyConcerns.For))!
            .GetParameters().Should().HaveCount(2);
    }
}
