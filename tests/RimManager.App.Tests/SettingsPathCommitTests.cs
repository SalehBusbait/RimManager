using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Settings has no commit step. Reset / Cancel / Save governed the four path fields alone
/// — the other six pages had been live since R6 — so three buttons spanning the window
/// spoke for one page of seven, and Cancel was a bare <c>Close()</c> that discarded
/// nothing because nothing was held back.
/// <para>
/// What replaces them is the rest of the window's rule: an edit is the commit. These pin
/// that, because "a control that is wired and does nothing still builds and still passes"
/// is this project's most expensive defect, and removing the button that used to do the
/// writing is exactly how a page ends up doing none.
/// </para>
/// </summary>
public sealed class SettingsPathCommitTests
{
    [Fact]
    public async Task Every_path_field_commits_on_edit()
    {
        var (vm, _, saved) = SettingsHarness.Build();

        vm.GameDir = "/moved/game";
        vm.ConfigDir = "/moved/config";
        vm.WorkshopDir = "/moved/workshop";
        vm.SteamCmdDir = "/moved/steamcmd";
        await vm.FlushPathsAsync();

        var last = saved.Last();
        last.GameDir.Should().Be("/moved/game");
        last.ConfigDir.Should().Be("/moved/config");
        last.WorkshopDir.Should().Be("/moved/workshop");
        last.SteamCmdDir.Should().Be("/moved/steamcmd");
    }

    /// <summary>
    /// Every field, one at a time. Written per-field rather than as the block above
    /// because a single missing <c>partial void On…Changed</c> hook is invisible when the
    /// others are checked together: the last write carries all four values regardless of
    /// which of them triggered it.
    /// </summary>
    [Theory]
    [InlineData("game")]
    [InlineData("config")]
    [InlineData("workshop")]
    [InlineData("steamcmd")]
    public async Task Editing_one_field_alone_writes(string field)
    {
        var (vm, _, saved) = SettingsHarness.Build();

        switch (field)
        {
            case "game": vm.GameDir = "/x"; break;
            case "config": vm.ConfigDir = "/x"; break;
            case "workshop": vm.WorkshopDir = "/x"; break;
            default: vm.SteamCmdDir = "/x"; break;
        }

        await vm.FlushPathsAsync();

        saved.Should().NotBeEmpty($"editing {field} alone must reach disk — with no Save "
                                  + "button left, a missing change hook is a field that "
                                  + "silently forgets what you typed");
    }

    /// <summary>
    /// Opening the window writes nothing. The fields are seeded through their backing
    /// fields precisely so construction raises nothing — otherwise merely looking at
    /// Settings would rewrite paths.json and stamp a backup.
    /// </summary>
    [Fact]
    public async Task Opening_settings_writes_nothing()
    {
        var (vm, _, saved) = SettingsHarness.Build();
        await vm.FlushPathsAsync();

        saved.Should().BeEmpty();
    }

    /// <summary>
    /// A path that is blank or whitespace is stored as null, not as "" — the same
    /// normalisation the old Save did. <c>PathProbe</c> distinguishes "not set" from
    /// "missing", and an empty string reaching disk turns the first into the second.
    /// </summary>
    [Fact]
    public async Task Blank_optional_paths_are_stored_as_not_set()
    {
        var (vm, _, saved) = SettingsHarness.Build();

        vm.WorkshopDir = "   ";
        await vm.FlushPathsAsync();

        saved.Last().WorkshopDir.Should().BeNull();
    }

    /// <summary>
    /// An unusable path is still written. The old Save refused while a required path was
    /// missing, which a continuous commit cannot do without becoming a page that silently
    /// declines to save what is on screen — the dead-control failure wearing a safety
    /// jacket. The verdict line under the field already says what is wrong and offers the
    /// fix, and what is on screen is what is on disk.
    /// </summary>
    [Fact]
    public async Task An_unusable_path_is_still_what_gets_written()
    {
        var (vm, _, saved) = SettingsHarness.Build();

        vm.GameDir = "/nowhere";
        await vm.FlushPathsAsync();

        vm.GameCheck.IsMissing.Should().BeTrue("the fixture's filesystem has no /nowhere");
        saved.Last().GameDir.Should().Be("/nowhere");
    }

    /// <summary>
    /// The window's own reload is what makes a path edit take effect, and it re-reads the
    /// file. Flushing has to be awaitable, or that reload races the last keystroke and
    /// hands the edit straight back.
    /// </summary>
    [Fact]
    public async Task Flush_awaits_the_write_in_flight()
    {
        var (vm, _, saved) = SettingsHarness.Build();

        vm.GameDir = "/last";
        vm.GameDir = "/actually-last";
        await vm.FlushPathsAsync();

        saved.Last().GameDir.Should().Be("/actually-last");
    }
}
