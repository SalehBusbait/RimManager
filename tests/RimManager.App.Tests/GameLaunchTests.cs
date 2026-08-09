using System;
using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Parsing the launch command (<c>2g</c>). Worth testing carefully because the two ways
/// to get it wrong are "the game does not start" and "something else starts instead".
/// </summary>
public sealed class GameLaunchTests
{
    [Fact]
    public void The_steam_form_splits_into_a_program_and_its_arguments()
    {
        var plan = GameLaunch.Parse("steam -applaunch 294100 %args%", extraArgs: null)!;

        plan.FileName.Should().Be("steam");
        plan.Arguments.Should().Equal("-applaunch", "294100");
    }

    /// <summary>
    /// The placeholder expands to nothing when there are no extra arguments — not to an
    /// empty string, which the game would receive as a blank argument.
    /// </summary>
    [Fact]
    public void No_extra_arguments_leaves_nothing_behind()
    {
        GameLaunch.Parse("game %args%", "")!.Arguments.Should().BeEmpty();
        GameLaunch.Parse("game %args%", "   ")!.Arguments.Should().BeEmpty();
        GameLaunch.Parse("game %args%", null)!.Arguments.Should().BeEmpty();
    }

    /// <summary>
    /// <c>%args%</c> is a placeholder, not an append. Steam reads anything after the
    /// AppID itself, so arguments tacked on the end would never reach RimWorld.
    /// </summary>
    [Fact]
    public void Extra_arguments_land_where_the_placeholder_is_not_at_the_end()
    {
        var plan = GameLaunch.Parse("steam -applaunch 294100 %args% -last", "-popupwindow")!;

        plan.Arguments.Should().Equal("-applaunch", "294100", "-popupwindow", "-last");
    }

    /// <summary>
    /// The failure that matters most. "C:\Program Files\RimWorld\game.exe" unquoted
    /// becomes the program "C:\Program" — which either fails or, on a machine where such
    /// a file exists, runs something the user did not ask for.
    /// </summary>
    [Fact]
    public void A_quoted_path_with_spaces_stays_one_program()
    {
        var plan = GameLaunch.Parse("\"C:\\Program Files\\RimWorld\\RimWorldWin64.exe\" %args%", null)!;

        plan.FileName.Should().Be("C:\\Program Files\\RimWorld\\RimWorldWin64.exe");
        plan.Arguments.Should().BeEmpty();
    }

    /// <summary>
    /// Quotes are stripped, because ProcessStartInfo.ArgumentList re-quotes each argument
    /// itself. Leaving them in makes the path literally contain a quote character.
    /// </summary>
    [Fact]
    public void Quotes_are_removed_rather_than_passed_through()
    {
        var plan = GameLaunch.Parse("game \"two words\" plain", null)!;

        plan.Arguments.Should().Equal("two words", "plain");
    }

    [Fact]
    public void An_empty_command_is_no_plan_rather_than_a_guess()
    {
        GameLaunch.Parse(null, "-x").Should().BeNull();
        GameLaunch.Parse("", "-x").Should().BeNull();
        GameLaunch.Parse("   ", "-x").Should().BeNull();
    }

    /// <summary>
    /// The bug that shipped. The mockup's command is <c>steam -applaunch 294100</c>, which
    /// is a LINUX command: only there is <c>steam</c> on PATH. On Windows it installs to
    /// Program Files and adds nothing to PATH, so the default could never run — verified
    /// on the developer's machine, where "steam" does not resolve and steam.exe sits in
    /// "C:\Program Files (x86)\Steam".
    /// </summary>
    [Fact]
    public void A_steam_install_names_steams_executable_rather_than_trusting_PATH()
    {
        var template = GameLaunch.DefaultTemplate(
            "/games/RimWorld", isSteamInstall: true,
            steamExe: @"C:\Program Files (x86)\Steam\steam.exe");

        var plan = GameLaunch.Parse(template, null)!;

        plan.FileName.Should().Be(@"C:\Program Files (x86)\Steam\steam.exe",
            "a bare 'steam' only resolves on Linux");
        plan.Arguments.Should().Equal("-applaunch", GameLaunch.AppId);
    }

    /// <summary>
    /// With no Steam to name, running the game directly is the option that actually
    /// starts something — a command referring to a program that is not there is not.
    /// </summary>
    [Fact]
    public void Without_steam_it_falls_back_to_the_game_executable()
    {
        var template = GameLaunch.DefaultTemplate(
            "/games/RimWorld", isSteamInstall: true, steamExe: null);

        template.Should().NotContain("-applaunch");
        GameLaunch.Parse(template, null)!.FileName.Should().StartWith("/games/RimWorld");
    }

    [Fact]
    public void A_non_steam_install_runs_the_game_directly()
    {
        var local = GameLaunch.DefaultTemplate("/games/RimWorld", isSteamInstall: false);

        local.Should().NotContain("steam");
        local.Should().Contain("RimWorld").And.EndWith(GameLaunch.ArgsPlaceholder);
    }

    /// <summary>With no game folder there is nothing to point at.</summary>
    [Fact]
    public void No_game_folder_falls_back_to_steam()
    {
        GameLaunch.DefaultTemplate(null, isSteamInstall: false)
            .Should().Be(GameLaunch.SteamTemplate);
    }

    /// <summary>
    /// An already-saved copy of the un-runnable default is corrected rather than
    /// preserved. Seeding is normally once-only so an edited command survives, but there
    /// is nothing to respect about a command that cannot start anything.
    /// </summary>
    [Fact]
    public void The_unrunnable_default_is_reseeded_but_a_real_choice_is_not()
    {
        GameLaunch.NeedsReseeding(null).Should().BeTrue();
        GameLaunch.NeedsReseeding("").Should().BeTrue();
        GameLaunch.NeedsReseeding(@"C:\Steam\steam.exe -applaunch 294100 %args%").Should().BeFalse();
        GameLaunch.NeedsReseeding("my-launcher %args%").Should().BeFalse();

        if (!OperatingSystem.IsLinux())
        {
            GameLaunch.NeedsReseeding(GameLaunch.SteamTemplate).Should().BeTrue(
                "bare 'steam' cannot run off PATH here");
        }
    }

    /// <summary>A generated default has to survive its own parser.</summary>
    [Fact]
    public void The_default_for_a_path_with_spaces_round_trips()
    {
        var template = GameLaunch.DefaultTemplate("/games/Rim World", isSteamInstall: false);

        var plan = GameLaunch.Parse(template, null)!;

        plan.FileName.Should().StartWith("/games/Rim World");
        plan.FileName.Should().NotContain("\"");
    }
}
