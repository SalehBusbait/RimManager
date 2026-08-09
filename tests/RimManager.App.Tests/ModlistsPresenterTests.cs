using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// What Settings ▸ Modlists says. Pinned by test rather than by memory, because the
/// sentences a destructive confirmation shows are a promise.
/// </summary>
public sealed class ModlistsPresenterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The fear with a mod manager is that it will delete your mods. Every delete sentence
    /// has to say what does NOT go, not only what does.
    /// </summary>
    [Fact]
    public void Delete_names_what_survives_and_not_only_what_goes()
    {
        var text = ModlistsPresenter.DeleteConsequence("Heavily modded", 4, 12);

        text.Should().Contain("Heavily modded");
        text.Should().Contain("4 snapshots").And.Contain("12 saved mod-settings files");
        text.Should().Contain("mods").And.Contain("saves").And.Contain("game folder");
        text.Should().Contain("untouched");
    }

    [Fact]
    public void Delete_stays_readable_when_there_is_nothing_else_to_lose()
    {
        var text = ModlistsPresenter.DeleteConsequence("Testing", 0, 0);

        // D5 · curly quotes: the dialog's own title uses them, and the reader was meeting
        // “Testing” and 'Testing' in one dialog.
        text.Should().Contain("Deletes the “Testing” list.");
        text.Should().NotContain("'", "the house style is typographic quotes");
        text.Should().NotContain("0 snapshots", "counting nothing reads as a bug");
    }

    [Theory]
    [InlineData(1, 0, "1 snapshot")]
    [InlineData(0, 1, "1 saved mod-settings file")]
    public void Singulars_are_not_pluralised(int snapshots, int settings, string expected) =>
        ModlistsPresenter.DeleteConsequence("x", snapshots, settings)
            .Should().Contain(expected).And.NotContain(expected + "s");

    /// <summary>A greyed control with no reason is one the user assumes is broken.</summary>
    [Fact]
    public void A_refused_delete_says_why_and_what_would_change_it()
    {
        ModlistsPresenter.WhyDeleteIsRefused(isDefault: true, totalLists: 5)
            .Should().Contain("default").And.Contain("Make another list the default");

        ModlistsPresenter.WhyDeleteIsRefused(isDefault: false, totalLists: 1)
            .Should().Contain("only modlist");

        ModlistsPresenter.WhyDeleteIsRefused(isDefault: false, totalLists: 3)
            .Should().BeNull("an allowed delete needs no excuse");
    }

    /// <summary>
    /// Turning capture ON starts writing into the game's config folder, so the off state
    /// has to say what it will do rather than merely that it is off.
    /// </summary>
    [Fact]
    public void The_mod_settings_card_explains_the_consequence_of_turning_it_on()
    {
        var off = ModlistsPresenter.ModSettingsSummary(captures: false, files: 0);
        off.Should().Contain("shares whatever mod settings the game currently has");
        off.Should().Contain("its own copy");

        ModlistsPresenter.ModSettingsSummary(captures: true, files: 0)
            .Should().Contain("first time you switch away");

        ModlistsPresenter.ModSettingsSummary(captures: true, files: 397)
            .Should().Contain("397 settings files");
    }

    [Fact]
    public void Duplicating_is_described_as_a_copy_not_a_switch()
    {
        var text = ModlistsPresenter.DuplicateConsequence("Main");

        text.Should().Contain("copy of “Main”");
        text.Should().Contain("without affecting the original");
    }

    [Fact]
    public void What_a_modlist_is_promises_it_never_touches_mods()
    {
        ModlistsPresenter.WhatIsAModlist
            .Should().Contain("never").And.Contain("deletes a mod");
    }

    [Theory]
    [InlineData(null, "never")]
    public void An_unopened_list_says_never(DateTimeOffset? when, string expected) =>
        ModlistsPresenter.LastUsed(when, Now).Should().Be(expected);

    [Fact]
    public void Ages_read_coarsely_because_the_question_is_roughly_when()
    {
        ModlistsPresenter.LastUsed(Now.AddSeconds(-30), Now).Should().Be("just now");
        ModlistsPresenter.LastUsed(Now.AddMinutes(-20), Now).Should().Be("20 minutes ago");
        ModlistsPresenter.LastUsed(Now.AddHours(-1), Now).Should().Be("1 hour ago");
        ModlistsPresenter.LastUsed(Now.AddDays(-1), Now).Should().Be("1 day ago");
        ModlistsPresenter.LastUsed(Now.AddDays(-70), Now).Should().Be("2 months ago");
    }
}
