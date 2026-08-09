using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.App.Tests.Fakes;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// 2k's game-not-found wording. Every sentence here is a claim about the user's disk,
/// and the two failures it distinguishes send someone looking in different places.
/// </summary>
public sealed class GameMissingPresenterTests
{
    private static StubFileSystem Fs() => new();

    private static StubFileSystem WithInstall() =>
        new StubFileSystem().WithDirectory("/game", "/game/Data", "/game/Data/Core");

    [Fact]
    public void A_real_install_does_not_raise_the_state()
    {
        var check = PathProbe.Game(WithInstall(), "/game");

        GameMissingPresenter.IsMissing(check).Should().BeFalse();
    }

    [Fact]
    public void A_vanished_folder_says_it_no_longer_exists()
    {
        var check = PathProbe.Game(Fs(), "/game");

        GameMissingPresenter.IsMissing(check).Should().BeTrue();
        GameMissingPresenter.Describe(check, "/game")
            .Should().Be("The configured install folder no longer exists.");
    }

    /// <summary>
    /// A folder that is present but gutted — a Steam "verify" gone wrong, a move that
    /// left the shell behind. Telling that user the folder does not exist sends them
    /// hunting for something that is on screen in front of them.
    /// </summary>
    [Fact]
    public void A_folder_that_is_no_longer_an_install_says_so_instead()
    {
        var fs = new StubFileSystem().WithDirectory("/game");

        var check = PathProbe.Game(fs, "/game");

        GameMissingPresenter.IsMissing(check).Should().BeTrue();
        GameMissingPresenter.Describe(check, "/game")
            .Should().Contain("not a RimWorld install any more")
            .And.Contain("Data/Core");
    }

    [Fact]
    public void No_folder_set_says_that_rather_than_blaming_the_disk()
    {
        var check = PathProbe.Game(Fs(), null);

        GameMissingPresenter.IsMissing(check).Should().BeTrue();
        // D4 · the word "instance" outlived the concept (N11 retired it), and this line
        // is shown twice for one event — the headline and the status bar.
        GameMissingPresenter.Describe(check, null)
            .Should().Be("No game folder is set yet.");
        GameMissingPresenter.Path(null).Should().Be("(no folder set)");
    }

    [Fact]
    public void No_user_facing_string_here_still_says_instance()
    {
        var check = PathProbe.Game(Fs(), null);

        GameMissingPresenter.Describe(check, null)
            .Should().NotContain("instance", "instances were retired in N11");
    }

    /// <summary>The path is compared character by character, so it is never shortened.</summary>
    [Fact]
    public void The_stale_path_is_shown_whole()
    {
        var deep = @"D:\Games\SteamLibrary\steamapps\common\RimWorld Beta 1.5 backup copy";

        GameMissingPresenter.Path(deep).Should().Be(deep);
    }
}
