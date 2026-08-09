using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The one destructive confirm (<c>2i</c>-6). The shape is the safety feature: someone who
/// has read one of these knows where the consequence sentence is and what the primary
/// button will say, so the shape is what these tests pin.
/// </summary>
public sealed class DestructiveConfirmTests
{
    private static DestructiveConfirmViewModel Vm(
        string? safety = null, bool safetyOn = true) =>
        new(new ConfirmRequest("Delete X?", "What goes and what does not.", "Delete X", safety, safetyOn));

    /// <summary>
    /// The most important behaviour here. Cancel, Escape and the title bar's ✕ all just
    /// close the window, so anything other than the primary button must leave it
    /// unconfirmed — a dismissed dialog can never read as consent.
    /// </summary>
    [Fact]
    public void A_dialog_that_was_merely_closed_is_not_consent()
    {
        var vm = Vm();

        vm.Confirmed.Should().BeFalse();
        vm.Result.Should().Be(ConfirmResult.Cancelled);
        vm.Result.Confirmed.Should().BeFalse();
    }

    [Fact]
    public void Only_the_primary_button_confirms()
    {
        var vm = Vm();

        vm.Confirm();

        vm.Result.Confirmed.Should().BeTrue();
    }

    /// <summary>
    /// The safety is hidden when there is none to offer. An unticked, unexplained
    /// checkbox reads as a step the user has skipped.
    /// </summary>
    [Fact]
    public void No_safety_offered_means_no_checkbox()
    {
        Vm(safety: null).HasSafety.Should().BeFalse();
        Vm(safety: "Export a backup first").HasSafety.Should().BeTrue();
    }

    [Fact]
    public void The_safety_choice_travels_with_the_confirmation()
    {
        var vm = Vm(safety: "Export a backup first", safetyOn: true);
        vm.Confirm();
        vm.Result.SafetyChosen.Should().BeTrue();

        var declined = Vm(safety: "Export a backup first", safetyOn: true);
        declined.SafetyChosen = false;
        declined.Confirm();
        declined.Result.SafetyChosen.Should().BeFalse();
    }

    /// <summary>A safety that was never offered must not report as chosen.</summary>
    [Fact]
    public void A_dialog_with_no_safety_never_reports_one()
    {
        var vm = Vm(safety: null, safetyOn: true);
        vm.Confirm();

        vm.Result.SafetyChosen.Should().BeFalse();
    }

    /// <summary>
    /// The primary button is the VERB (<c>2i</c>-6), never "OK". A button that says OK
    /// makes the reader reconstruct what they are agreeing to from the title.
    /// </summary>
    [Fact]
    public void The_primary_button_is_a_verb()
    {
        var vm = Vm();

        vm.Verb.Should().Be("Delete X");
        new[] { "OK", "Yes", "Confirm" }.Should().NotContain(vm.Verb);
    }

    /// <summary>A cancelled result carries no safety, whatever it was set to.</summary>
    [Fact]
    public void Cancelling_discards_the_safety_choice_too()
    {
        var vm = Vm(safety: "Export a backup first");

        vm.Result.Confirmed.Should().BeFalse();
        vm.Result.SafetyChosen.Should().BeFalse();
    }
}
