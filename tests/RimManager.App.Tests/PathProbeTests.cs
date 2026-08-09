using FluentAssertions;
using RimManager.App.Tests.Fakes;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Settings ▸ Paths validation (<c>1c</c>: "RimManager validates each path and shows
/// what it found").
/// <para>
/// The rule these pin is that a check reports what it FOUND, never merely that a
/// folder exists. A validator that says "✓" for an empty folder, or for a folder that
/// happens to be named RimWorld, sends someone hunting for a bug that is really a
/// wrong path — and it looks completely correct in a screenshot.
/// </para>
/// </summary>
public sealed class PathProbeTests
{
    private const string Game = "/games/RimWorld";

    private static StubFileSystem Install(params string[] dlc)
    {
        var fs = new StubFileSystem()
            .WithDirectory(Game, $"{Game}/Data", $"{Game}/Data/Core")
            .WithFile($"{Game}/Version.txt", "1.6.4871 rev590");

        foreach (var d in dlc) fs.WithDirectory($"{Game}/Data/{d}");
        return fs;
    }

    // --- the game folder ----------------------------------------------------

    [Fact]
    public void A_real_install_reports_its_version_and_its_dlc()
    {
        var check = PathProbe.Game(Install("Royalty", "Biotech"), Game);

        check.Verdict.Should().Be(PathVerdict.Ok);
        check.Message.Should().Be("RimWorld 1.6.4871 · 2 DLC found (Royalty, Biotech)");
    }

    /// <summary>The revision is noise in a settings field; the version is not.</summary>
    [Fact]
    public void The_version_drops_the_build_revision()
    {
        PathProbe.Game(Install(), Game).Message.Should().Contain("1.6.4871")
            .And.NotContain("rev590");
    }

    /// <summary>
    /// The single most valuable check on this screen. A folder can exist, be named
    /// RimWorld, and not be an install — pointing at the Steam library root does
    /// exactly that, and "folder exists" would wave it through.
    /// </summary>
    [Fact]
    public void A_folder_without_Data_Core_is_not_an_install_however_it_is_named()
    {
        var fs = new StubFileSystem().WithDirectory(Game, $"{Game}/Data");

        var check = PathProbe.Game(fs, Game);

        check.Verdict.Should().Be(PathVerdict.Missing);
        check.Message.Should().Contain("not a RimWorld install");
        check.Action.Should().Be("Auto-detect");
    }

    [Fact]
    public void A_missing_or_blank_game_folder_blocks_rather_than_warns()
    {
        PathProbe.Game(new StubFileSystem(), Game).Verdict.Should().Be(PathVerdict.Missing);
        PathProbe.Game(new StubFileSystem(), null).Verdict.Should().Be(PathVerdict.Missing);
        PathProbe.Game(new StubFileSystem(), "  ").Verdict.Should().Be(PathVerdict.Missing);
    }

    [Fact]
    public void An_install_with_no_dlc_says_so_rather_than_listing_nothing()
    {
        PathProbe.Game(Install(), Game).Message.Should().EndWith("no DLC found");
    }

    [Fact]
    public void A_missing_version_file_still_leaves_the_install_valid()
    {
        var fs = new StubFileSystem().WithDirectory(Game, $"{Game}/Data", $"{Game}/Data/Core");

        var check = PathProbe.Game(fs, Game);

        check.Verdict.Should().Be(PathVerdict.Ok);
        check.Message.Should().StartWith("RimWorld ·");
    }

    // --- the config folder --------------------------------------------------

    [Fact]
    public void A_config_folder_with_ModsConfig_is_ok()
    {
        var fs = new StubFileSystem().WithFile("/cfg/ModsConfig.xml", "<x/>");

        PathProbe.Config(fs, "/cfg").Should().Match<PathCheck>(
            c => c.IsOk && c.Message.Contains("backup on every Apply"));
    }

    /// <summary>
    /// Normal before RimWorld has ever run — so a warning, not an error. But it must
    /// not read as a clean bill either, or a genuinely wrong folder passes silently.
    /// </summary>
    [Fact]
    public void A_config_folder_without_ModsConfig_warns_rather_than_failing()
    {
        var fs = new StubFileSystem().WithDirectory("/cfg");

        var check = PathProbe.Config(fs, "/cfg");

        check.Verdict.Should().Be(PathVerdict.Warning);
        check.Message.Should().Contain("written on the first Apply");
    }

    // --- optional folders ---------------------------------------------------

    /// <summary>
    /// Optional paths must never reach Missing on being blank, or Save would be
    /// blocked for anyone who does not use Steam.
    /// </summary>
    [Fact]
    public void Blank_optional_paths_are_not_set_rather_than_missing()
    {
        PathProbe.Workshop(new StubFileSystem(), null).Verdict.Should().Be(PathVerdict.NotSet);
        PathProbe.SteamCmd(new StubFileSystem(), null).Verdict.Should().Be(PathVerdict.NotSet);
        PathProbe.LocalMods(new StubFileSystem(), null).Verdict.Should().Be(PathVerdict.NotSet);
    }

    [Fact]
    public void A_workshop_folder_that_is_set_but_absent_offers_to_locate_it()
    {
        var check = PathProbe.Workshop(new StubFileSystem(), "/steam/workshop/294100");

        check.Verdict.Should().Be(PathVerdict.Missing);
        check.Message.Should().Contain("will not be listed");
        check.Action.Should().Be("Locate…");
    }

    /// <summary>
    /// Mods are counted by About/About.xml, not by folder: a Workshop directory is
    /// full of stray downloads and half-extracted archives, and counting those would
    /// report a number the mod list never matches.
    /// </summary>
    [Fact]
    public void Mods_are_counted_by_their_About_xml_not_by_folder()
    {
        var fs = new StubFileSystem()
            .WithDirectory("/ws")
            .WithFile("/ws/1234/About/About.xml", "<ModMetaData/>")
            .WithFile("/ws/5678/About/About.xml", "<ModMetaData/>")
            .WithDirectory("/ws/junk");

        var check = PathProbe.Workshop(fs, "/ws");

        check.Verdict.Should().Be(PathVerdict.Ok);
        check.Message.Should().Be("2 Workshop mods installed");
    }

    /// <summary>
    /// The count is of folders on disk, so it must not claim to be a subscription count.
    /// Settings ▸ Integrations reports the same fact from Steam's own manifest and says
    /// "installed"; two pages using different words for one number is how a user
    /// concludes one of them is wrong.
    /// </summary>
    [Fact]
    public void The_workshop_count_says_installed_never_subscribed()
    {
        var fs = new StubFileSystem()
            .WithFile("/ws/1234/About/About.xml", "<ModMetaData/>");

        foreach (var message in new[]
                 {
                     PathProbe.Workshop(fs, "/ws").Message,
                     PathProbe.Workshop(new StubFileSystem().WithDirectory("/ws"), "/ws").Message,
                     PathProbe.Workshop(fs, null).Message,
                 })
        {
            message.Should().NotContain("subscrib",
                "this counts About.xml folders; a subscription with nothing downloaded "
                + "would not appear, and reading real subscriptions needs an account call "
                + "we deliberately do not make");
        }
    }

    /// <summary>
    /// 1c reports git on the LOCAL mods line, because local mods are the only place a
    /// clone can be — see GitServiceTests for why a `.git` in a Workshop folder is not one.
    /// </summary>
    [Fact]
    public void Local_mods_report_how_many_are_tracked_by_git()
    {
        var fs = new StubFileSystem()
            .WithFile("/mods/A/About/About.xml", "<ModMetaData/>")
            .WithFile("/mods/B/About/About.xml", "<ModMetaData/>");

        PathProbe.LocalMods(fs, "/mods", trackedByGit: 1).Message
            .Should().Be("2 local mods · 1 tracked by git");

        PathProbe.LocalMods(fs, "/mods", trackedByGit: 0).Message
            .Should().Be("2 local mods",
                "a permanent '0 tracked by git' is noise on the installs that never cloned anything");
    }

    [Fact]
    public void An_empty_but_present_folder_warns_rather_than_reporting_success()
    {
        var fs = new StubFileSystem().WithDirectory("/ws");

        PathProbe.Workshop(fs, "/ws").Verdict.Should().Be(PathVerdict.Warning);
        PathProbe.LocalMods(fs.WithDirectory("/mods"), "/mods").Verdict.Should().Be(PathVerdict.Warning);
    }

    [Fact]
    public void One_mod_is_singular()
    {
        var fs = new StubFileSystem()
            .WithDirectory("/mods")
            .WithFile("/mods/Only/About/About.xml", "<ModMetaData/>");

        PathProbe.LocalMods(fs, "/mods").Message.Should().Be("1 local mod");
    }

    /// <summary>
    /// SteamCMD's verdict carries NO inline action. 1c puts "Install for me" in the field
    /// row as a real button beside Browse, which is the only place it can report progress
    /// on a ~250 MB download — as an inline link it was a dead affordance for four phases.
    /// </summary>
    [Fact]
    public void SteamCmd_leaves_installing_to_the_button_in_the_row()
    {
        PathProbe.SteamCmd(new StubFileSystem(), null).HasAction.Should().BeFalse();
        PathProbe.SteamCmd(new StubFileSystem(), "/nope").Should().Match<PathCheck>(
            c => c.IsMissing && !c.HasAction);
    }
}
