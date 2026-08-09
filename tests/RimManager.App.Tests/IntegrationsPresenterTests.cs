using System.Collections.Generic;
using FluentAssertions;
using RimManager.App.Services;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The Integrations cards (<c>2g</c>). These assert <b>wording</b>, which is unusual for a
/// unit test and deliberate here: every line on that page is a claim about the user's
/// machine, and the failure mode is a card that reads as a fact while the number
/// underneath means something else — "342 subscribed items" over a count of items
/// <i>installed</i>.
/// </summary>
public sealed class IntegrationsPresenterTests
{
    private static IntegrationStatus Status(
        bool steam = false, int? items = null,
        bool steamCmd = false, string steamCmdDir = "/steamcmd", long bytes = 0,
        string? git = null, string? gitPath = null, int repos = 0, int dirty = 0) =>
        new(steam, items, steamCmd, steamCmdDir, bytes, git, gitPath, repos, dirty);

    // --- Steam --------------------------------------------------------------

    [Fact]
    public void Steam_says_installed_not_subscribed()
    {
        var text = IntegrationsPresenter.SteamHeadline(Status(steam: true, items: 342));

        text.Should().Be("Steam client running · 342 Workshop items installed");
        text.Should().NotContain("subscribed",
            "the count comes from Steam's install manifest; a subscription with nothing "
            + "downloaded is invisible to us and must not be claimed");
    }

    /// <summary>
    /// No manifest is not "you have nothing". A user pointed at the wrong Workshop folder
    /// would otherwise read a confident zero.
    /// </summary>
    [Fact]
    public void A_missing_manifest_is_not_reported_as_zero_items()
    {
        IntegrationsPresenter.SteamHeadline(Status(steam: true, items: null))
            .Should().Be("Steam client running · no Workshop manifest found");

        IntegrationsPresenter.SteamHeadline(Status(steam: true, items: 0))
            .Should().Be("Steam client running · no Workshop items installed");
    }

    [Fact]
    public void Steam_not_running_is_reported_as_a_state_not_a_failure()
    {
        IntegrationsPresenter.SteamHeadline(Status(items: 12))
            .Should().StartWith("Steam client not running");

        IntegrationsPresenter.SteamPill(Status()).Should().Be("not running");
        IntegrationsPresenter.SteamPill(Status(steam: true)).Should().Be("running");
    }

    /// <summary>
    /// The pill never claims a connection. RimManager holds no Steam session — what it
    /// does with a running client is hand it a <c>steam://</c> URL.
    /// </summary>
    [Fact]
    public void The_pill_does_not_claim_a_connection_we_do_not_have()
    {
        IntegrationsPresenter.SteamPill(Status(steam: true)).Should().NotContain("connect");
        IntegrationsPresenter.SteamUses.Should().NotContain("unsubscribe",
            "unsubscribing needs the Steamworks SDK; we only open URLs");
    }

    [Fact]
    public void Item_count_is_singular_at_one()
    {
        IntegrationsPresenter.SteamHeadline(Status(steam: true, items: 1))
            .Should().Be("Steam client running · 1 Workshop item installed");
    }

    // --- SteamCMD -----------------------------------------------------------

    [Fact]
    public void SteamCmd_names_its_target_path_whether_or_not_it_is_installed()
    {
        IntegrationsPresenter.SteamCmdDetail(Status(steamCmdDir: "/rm/steamcmd"))
            .Should().Contain("not installed").And.Contain("/rm/steamcmd");

        IntegrationsPresenter
            .SteamCmdDetail(Status(steamCmd: true, steamCmdDir: "/rm/steamcmd", bytes: 284_000_000))
            .Should().StartWith("installed · ").And.Contain("/rm/steamcmd");
    }

    // --- git ----------------------------------------------------------------

    [Fact]
    public void Git_absent_is_stated_plainly_and_the_path_line_says_what_to_do()
    {
        var status = Status();

        IntegrationsPresenter.GitHeadline(status).Should().Be("git not found on PATH");
        IntegrationsPresenter.GitPathLine(status).Should().Contain("install git");
    }

    [Fact]
    public void Git_headline_counts_tracked_repos_and_singularises()
    {
        IntegrationsPresenter.GitHeadline(Status(git: "2.45.2", repos: 2))
            .Should().Be("git 2.45.2 · 2 tracked mods");

        IntegrationsPresenter.GitHeadline(Status(git: "2.45.2", repos: 1))
            .Should().Be("git 2.45.2 · 1 tracked mod");

        IntegrationsPresenter.GitHeadline(Status(git: "2.45.2"))
            .Should().Be("git 2.45.2 · no tracked mods");
    }

    /// <summary>
    /// The dirty clause appears only when there is something dirty. A permanent
    /// "0 with uncommitted changes" is noise on the vast majority of installs, which
    /// track no repositories at all.
    /// </summary>
    [Fact]
    public void The_dirty_clause_is_stated_only_when_there_is_something_to_state()
    {
        IntegrationsPresenter.GitHeadline(Status(git: "2.45.2", repos: 3, dirty: 0))
            .Should().NotContain("uncommitted");

        IntegrationsPresenter.GitHeadline(Status(git: "2.45.2", repos: 3, dirty: 1))
            .Should().EndWith("· 1 with uncommitted changes");
    }

    // --- PATH resolution ----------------------------------------------------

    /// <summary>
    /// Resolving git from PATH ourselves instead of shelling out to where/which. The
    /// thing worth testing is the splitting, which differs per platform.
    /// </summary>
    [Fact]
    public void Git_is_found_at_the_first_path_entry_that_holds_it()
    {
        var present = new HashSet<string>
        {
            Path("/opt/bin", "git"),
            Path("/usr/bin", "git"),
        };

        var path = string.Join(System.IO.Path.PathSeparator, ["/nowhere", "/opt/bin", "/usr/bin"]);

        GitService.FindOnPath(path, present.Contains)
            .Should().Be(Path("/opt/bin", "git"), "PATH order is the answer");
    }

    [Fact]
    public void A_path_with_no_git_and_an_empty_path_both_resolve_to_null()
    {
        GitService.FindOnPath("/nowhere", _ => false).Should().BeNull();
        GitService.FindOnPath(null, _ => true).Should().BeNull();
        GitService.FindOnPath("   ", _ => true).Should().BeNull();
    }

    /// <summary>
    /// Windows writes quoted PATH entries often enough that an unstripped quote would
    /// make git undiscoverable on exactly the platform most users are on. The fixture
    /// path carries the space that quoting exists for but no drive letter: FindOnPath
    /// splits on the platform's own separator, and on Linux and macOS that separator
    /// is the colon inside "C:" — a Windows-shaped entry is unsplittable there by
    /// design, and this test runs on all three platforms.
    /// </summary>
    [Fact]
    public void Quoted_path_entries_are_unwrapped()
    {
        var target = Path("/Program Files/Git/cmd", "git.exe");

        GitService.FindOnPath("\"/Program Files/Git/cmd\"", p => p == target)
            .Should().Be(target);
    }

    private static string Path(string dir, string file) => System.IO.Path.Combine(dir, file);

    // --- path eliding -------------------------------------------------------
    // A card shows a path inside a fixed-width column. The eliding happens in the
    // STRING because doing it with TextTrimming does not constrain layout: a
    // non-wrapping TextBlock measures at its full natural width, so the star column
    // demanded more than the window had and the card's buttons were pushed off the
    // right edge. That shipped, and it is why these budgets are tested.

    [Fact]
    public void A_path_within_budget_is_left_alone()
    {
        IntegrationsPresenter.ShortenPath(@"C:\Git\git.exe", 40)
            .Should().Be(@"C:\Git\git.exe");
    }

    [Fact]
    public void A_budget_of_zero_means_do_not_shorten()
    {
        var full = @"C:\Users\PC\AppData\Local\RimManager\steamcmd";

        IntegrationsPresenter.ShortenPath(full, 0).Should().Be(full,
            "the tooltip asks for the whole value");
    }

    /// <summary>
    /// The MIDDLE goes, never the tail. Trimming the end would leave every Windows path
    /// reading "C:\Users\PC\AppData\Local\…" — the half that is identical for everyone.
    /// </summary>
    [Fact]
    public void A_long_path_keeps_its_root_and_its_end()
    {
        var result = IntegrationsPresenter.ShortenPath(
            @"C:\Users\PC\AppData\Local\RimManager\steamcmd", 40);

        result.Should().StartWith(@"C:\…");
        result.Should().EndWith(@"\steamcmd");
        result.Length.Should().BeLessThanOrEqualTo(40);
    }

    [Fact]
    public void Forward_slash_paths_elide_the_same_way()
    {
        var result = IntegrationsPresenter.ShortenPath(
            "/home/pc/.local/share/rimmanager/steamcmd/linux32", 30);

        result.Should().EndWith("/linux32");
        result.Length.Should().BeLessThanOrEqualTo(30);
    }

    /// <summary>A single segment longer than the budget cannot be split sensibly; showing
    /// its end beats showing nothing, because the end is the part that differs.</summary>
    [Fact]
    public void An_unsplittable_path_still_fits_its_budget()
    {
        var result = IntegrationsPresenter.ShortenPath(new string('x', 80), 20);

        result.Length.Should().BeLessThanOrEqualTo(20);
    }

    /// <summary>
    /// The whole point: whatever the path, the card's line stays short enough that the
    /// column it sits in does not have to grow.
    /// </summary>
    [Fact]
    public void The_steamcmd_line_stays_bounded_however_deep_the_install_is()
    {
        var deep = @"C:\Users\SomebodyWithAVeryLongName\AppData\Local\Programs\RimManager\vendor\steamcmd";

        var shown = IntegrationsPresenter.SteamCmdDetail(Status(steamCmdDir: deep), pathBudget: 40);
        var tip = IntegrationsPresenter.SteamCmdDetail(Status(steamCmdDir: deep));

        shown.Should().NotContain(deep);
        shown.Should().Contain("steamcmd", "the end of the path is the identifying half");
        tip.Should().Contain(deep, "the tooltip is where the untruncated value lives");
    }
}
