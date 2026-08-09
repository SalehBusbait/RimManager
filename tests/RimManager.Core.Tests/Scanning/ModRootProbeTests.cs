using FluentAssertions;
using RimManager.Core.Scanning;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Scanning;

/// <summary>
/// Noticing a mod folder appear or disappear.
/// <para>
/// Every case here is one a <c>FileSystemWatcher</c> would have made untestable — which is
/// most of why the mechanism is a poll. The one that decided it is
/// <see cref="A_folder_built_in_place_is_reported_once_it_becomes_readable"/>: measured on
/// Windows, a folder built in place raises exactly one <c>Created</c> while it is still
/// empty and then nothing ever again, so an event-driven version reports the mod at the one
/// moment it cannot be read and never mentions it afterwards.
/// </para>
/// </summary>
public sealed class ModRootProbeTests
{
    private const string Workshop = "/steam/workshop/294100";
    private const string Local = "/game/Mods";

    private static readonly string[] BothRoots = [Workshop, Local];
    private static readonly string[] WorkshopOnly = [Workshop];

    private readonly InMemoryFileSystem _fs = new(new FixedClock(DateTimeOffset.UnixEpoch));
    private readonly ModRootProbe _probe;

    public ModRootProbeTests()
    {
        _probe = new ModRootProbe(_fs);
        _fs.CreateDirectory(Workshop);
        _fs.CreateDirectory(Local);
    }

    /// <summary>A finished mod: a folder with an About.xml RimWorld could read.</summary>
    private void InstallMod(string root, string name) =>
        _fs.AddFile($"{root}/{name}/About/About.xml", "<ModMetaData><packageId>x.y</packageId></ModMetaData>");

    /// <summary>A download in progress: the folder exists, the metadata does not.</summary>
    private void BeginInstall(string root, string name) => _fs.CreateDirectory($"{root}/{name}");

    private void Uninstall(string root, string name) => _fs.DeleteDirectory($"{root}/{name}", recursive: true);

    /// <summary>Polls until a change is reported, or gives up. Returns what it saw.</summary>
    private ModRootChanges PollUntilReported(int maxPolls = 6)
    {
        for (var i = 0; i < maxPolls; i++)
        {
            var changes = _probe.Poll(BothRoots);
            if (changes.Any) return changes;
        }

        return ModRootChanges.None;
    }

    // --- the baseline --------------------------------------------------------

    /// <summary>
    /// The first poll must announce nothing. Otherwise every launch would report the whole
    /// install as newly arrived — 564 mods on the developer's machine.
    /// </summary>
    [Fact]
    public void The_first_poll_of_a_root_reports_nothing()
    {
        for (var i = 0; i < 20; i++) InstallMod(Workshop, $"mod{i}");

        _probe.Poll(BothRoots).Any.Should().BeFalse();
        _probe.Poll(BothRoots).Any.Should().BeFalse("and it stays quiet while nothing changes");
    }

    // --- a mod arriving ------------------------------------------------------

    [Fact]
    public void A_mod_that_arrives_complete_is_reported()
    {
        _probe.Poll(BothRoots);

        InstallMod(Workshop, "3339999111");
        var changes = PollUntilReported();

        changes.Added.Should().Equal("3339999111");
        changes.Removed.Should().BeEmpty();
    }

    /// <summary>
    /// It must not be reported on the very first sighting. A settle of two polls is what
    /// keeps a mod that is still being unzipped out of the notice.
    /// </summary>
    [Fact]
    public void A_new_mod_waits_for_the_settle_before_being_reported()
    {
        _probe.Poll(BothRoots);
        InstallMod(Workshop, "abc");

        _probe.Poll(BothRoots).Any.Should().BeFalse("one sighting is not enough");
        _probe.Poll(BothRoots).Added.Should().Equal("abc");
    }

    /// <summary>
    /// <b>The case that chose polling over a watcher.</b> A folder created empty and filled
    /// later raises one event, while empty, and never another. Here it is simply picked up
    /// on the poll after it becomes readable.
    /// </summary>
    [Fact]
    public void A_folder_built_in_place_is_reported_once_it_becomes_readable()
    {
        _probe.Poll(BothRoots);

        BeginInstall(Workshop, "slow");
        _probe.Poll(BothRoots).Any.Should().BeFalse("an empty folder is not a mod");
        _probe.Poll(BothRoots).Any.Should().BeFalse("and still is not, however long it sits there");

        InstallMod(Workshop, "slow");

        PollUntilReported().Added.Should().ContainSingle()
            .Which.Should().Be("slow",
                "a watcher would have gone silent after the folder was created empty");
    }

    /// <summary>A folder that never becomes readable is never reported at all.</summary>
    [Fact]
    public void A_folder_that_never_becomes_a_mod_is_never_reported()
    {
        _probe.Poll(BothRoots);
        BeginInstall(Workshop, "junk");

        for (var i = 0; i < 25; i++) _probe.Poll(BothRoots).Added.Should().BeEmpty();
    }

    /// <summary>
    /// It is counted as pending while it is being waited for, so a caller can say "and one
    /// still arriving" rather than silently dropping it — then it stops being probed.
    /// </summary>
    [Fact]
    public void An_abandoned_download_is_eventually_given_up_on()
    {
        _probe.Poll(BothRoots);
        BeginInstall(Workshop, "abandoned");

        _probe.Poll(BothRoots).Pending.Should().Be(1, "it is being waited for");

        for (var i = 0; i < ModRootProbe.MaxPollsWaitingToBecomeReadable + 2; i++) _probe.Poll(BothRoots);

        _probe.Poll(BothRoots).Pending.Should().Be(0,
            "Steam leaves failed downloads behind for ever; probing one until the app closes "
            + "is a cost with no payer");
    }

    /// <summary>A cancelled download leaves no trace — it was never news.</summary>
    [Fact]
    public void A_download_that_is_cancelled_reports_nothing()
    {
        _probe.Poll(BothRoots);

        BeginInstall(Workshop, "cancelled");
        _probe.Poll(BothRoots);
        Uninstall(Workshop, "cancelled");

        for (var i = 0; i < 4; i++) _probe.Poll(BothRoots).Any.Should().BeFalse();
    }

    // --- a mod leaving -------------------------------------------------------

    [Fact]
    public void An_uninstalled_mod_is_reported()
    {
        InstallMod(Workshop, "goingaway");
        _probe.Poll(BothRoots);

        Uninstall(Workshop, "goingaway");
        var changes = PollUntilReported();

        changes.Removed.Should().Equal("goingaway");
        changes.Added.Should().BeEmpty();
    }

    /// <summary>
    /// An update that deletes the folder and puts it back must read as nothing at all. Two
    /// consecutive absences are required precisely so a blink does not become a removal
    /// followed by an addition of the same mod.
    /// </summary>
    [Fact]
    public void A_folder_that_blinks_out_and_back_reports_nothing()
    {
        InstallMod(Workshop, "updating");
        _probe.Poll(BothRoots);

        Uninstall(Workshop, "updating");
        _probe.Poll(BothRoots).Any.Should().BeFalse("one absence is not a removal");

        InstallMod(Workshop, "updating");

        for (var i = 0; i < 4; i++)
            _probe.Poll(BothRoots).Any.Should().BeFalse("it never actually went anywhere");
    }

    // --- what it must NOT see ------------------------------------------------

    /// <summary>
    /// The scope, pinned. Everything inside a mod folder is invisible: an update rewrites
    /// files in place, and updates are the update checker's job — it answers them exactly
    /// rather than guessing from file writes.
    /// </summary>
    [Fact]
    public void Changes_inside_a_mod_folder_are_invisible()
    {
        InstallMod(Workshop, "busy");
        _probe.Poll(BothRoots);

        for (var i = 0; i < 60; i++) _fs.AddFile($"{Workshop}/busy/Assemblies/a{i}.dll", "binary");
        _fs.AddFile($"{Workshop}/busy/About/About.xml", "<ModMetaData><name>renamed</name></ModMetaData>");
        _fs.AddFile($"{Workshop}/busy/1.6/Defs/Thing.xml", "<Defs/>");

        for (var i = 0; i < 4; i++)
            _probe.Poll(BothRoots).Any.Should().BeFalse("sixty writes inside a mod are not a mod arriving");
    }

    // --- roots ---------------------------------------------------------------

    [Fact]
    public void Both_roots_are_watched_and_reported_together()
    {
        _probe.Poll(BothRoots);

        InstallMod(Workshop, "fromsteam");
        InstallMod(Local, "handcopied");

        var changes = PollUntilReported();
        changes.Added.Should().BeEquivalentTo(["fromsteam", "handcopied"]);
    }

    /// <summary>
    /// The config folder can be edited in Settings while the app runs. A root that stops
    /// being configured is forgotten, so re-adding it later baselines afresh rather than
    /// announcing everything in it.
    /// </summary>
    [Fact]
    public void A_root_that_stops_being_configured_is_forgotten()
    {
        InstallMod(Local, "local1");
        _probe.Poll(BothRoots);

        _probe.Poll(WorkshopOnly);          // Local drops out of the configuration
        InstallMod(Local, "local2");

        for (var i = 0; i < 4; i++)
            _probe.Poll(BothRoots).Any.Should().BeFalse("re-adding a root baselines it, it does not report it");
    }

    [Fact]
    public void A_missing_root_is_not_an_error()
    {
        var act = () => _probe.Poll(["/nowhere/at/all"]);

        act.Should().NotThrow("an unplugged drive degrades to Refresh, it does not break the app");
        act().Any.Should().BeFalse();
    }

    // --- rebaseline ----------------------------------------------------------

    /// <summary>
    /// After a rescan, what the user has just been shown becomes the new truth. Without this
    /// the app's own SteamCMD download would surface a moment later as somebody else's news.
    /// </summary>
    [Fact]
    public void Rebaselining_after_a_rescan_forgets_what_was_pending()
    {
        _probe.Poll(BothRoots);
        InstallMod(Workshop, "wedownloadedthis");

        _probe.Rebaseline(BothRoots);

        for (var i = 0; i < 4; i++)
            _probe.Poll(BothRoots).Any.Should().BeFalse("the app must not report the thing the app just did");
    }

    [Fact]
    public void Rebaselining_still_notices_what_comes_after_it()
    {
        _probe.Poll(BothRoots);
        _probe.Rebaseline(BothRoots);

        InstallMod(Workshop, "later");
        PollUntilReported().Added.Should().Equal("later");
    }

    /// <summary>
    /// A mod half-installed when the app starts is still an event when it finishes. The
    /// baseline records it as waiting, not as established.
    /// </summary>
    [Fact]
    public void A_download_already_running_at_startup_is_reported_when_it_lands()
    {
        BeginInstall(Workshop, "inflight");
        _probe.Poll(BothRoots);            // baseline: present, but not a mod

        InstallMod(Workshop, "inflight");
        PollUntilReported().Added.Should().Equal("inflight");
    }
}
