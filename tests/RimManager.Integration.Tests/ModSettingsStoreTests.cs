using FluentAssertions;
using RimManager.Storage;
using RimManager.Storage.Repositories;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Mod settings captured and restored through real files — the one thing an instance
/// could genuinely have isolated.
/// </summary>
public sealed class ModSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "rimmanager-modsettings-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly PhysicalFileSystem _fs = new();
    private readonly string _config;

    public ModSettingsStoreTests()
    {
        _config = Path.Combine(_root, "Config");
        Directory.CreateDirectory(_config);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private ModSettingsStore Store() => new(_fs, _root);

    private void GivenConfigFile(string name, string contents) =>
        File.WriteAllText(Path.Combine(_config, name), contents);

    private string ConfigText(string name) => File.ReadAllText(Path.Combine(_config, name));

    [Fact]
    public async Task Capture_takes_mod_files_and_leaves_the_games_own_alone()
    {
        GivenConfigFile("Mod_123_Thing.xml", "<settings>tuned</settings>");
        GivenConfigFile("CameraPlusColors.txt", "colours");
        GivenConfigFile("Prefs.xml", "<prefs>1920x1080</prefs>");
        GivenConfigFile("ModsConfig.xml", "<activeMods />");

        var capture = await Store().CaptureAsync("list1", _config);

        capture.Files.Should().Be(2);
        var dir = Store().DirectoryFor("list1");
        File.Exists(Path.Combine(dir, "Mod_123_Thing.xml")).Should().BeTrue();
        File.Exists(Path.Combine(dir, "CameraPlusColors.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dir, "Prefs.xml")).Should().BeFalse(
            "restoring that on a switch would change the player's screen resolution");
        File.Exists(Path.Combine(dir, "ModsConfig.xml")).Should().BeFalse(
            "the load order belongs to the apply pipeline; two writers would fight");
    }

    [Fact]
    public async Task Restore_writes_the_captured_settings_back()
    {
        GivenConfigFile("Mod_123_Thing.xml", "<settings>list-one</settings>");
        await Store().CaptureAsync("list1", _config);

        GivenConfigFile("Mod_123_Thing.xml", "<settings>list-two</settings>");

        var written = await Store().RestoreAsync("list1", _config);

        written.Should().Be(1);
        ConfigText("Mod_123_Thing.xml").Should().Contain("list-one");
    }

    /// <summary>The whole point: two lists tuning the same mod differently.</summary>
    [Fact]
    public async Task Two_lists_keep_separate_settings_for_the_same_mod()
    {
        var store = Store();

        GivenConfigFile("Mod_123_Thing.xml", "<settings>alpha</settings>");
        await store.CaptureAsync("alpha", _config);

        GivenConfigFile("Mod_123_Thing.xml", "<settings>beta</settings>");
        await store.CaptureAsync("beta", _config);

        await store.RestoreAsync("alpha", _config);
        ConfigText("Mod_123_Thing.xml").Should().Contain("alpha");

        await store.RestoreAsync("beta", _config);
        ConfigText("Mod_123_Thing.xml").Should().Contain("beta");
    }

    /// <summary>
    /// A file present in Config but absent from the incoming snapshot may belong to a mod
    /// both lists share whose settings the incoming list never captured. Deleting it would
    /// destroy tuning nothing asked us to touch.
    /// </summary>
    [Fact]
    public async Task Restore_never_deletes_a_file_it_does_not_have()
    {
        GivenConfigFile("Mod_123_Thing.xml", "<settings>captured</settings>");
        await Store().CaptureAsync("list1", _config);

        GivenConfigFile("Mod_999_Other.xml", "<settings>untouched</settings>");
        await Store().RestoreAsync("list1", _config);

        ConfigText("Mod_999_Other.xml").Should().Contain("untouched");
    }

    /// <summary>
    /// A stale file left from a previous capture would be handed back as though current —
    /// the subtlest possible way to return settings the user had already changed.
    /// </summary>
    [Fact]
    public async Task Recapturing_replaces_rather_than_accumulates()
    {
        GivenConfigFile("Mod_a.xml", "a");
        GivenConfigFile("Mod_b.xml", "b");
        await Store().CaptureAsync("list1", _config);

        File.Delete(Path.Combine(_config, "Mod_b.xml"));
        var capture = await Store().CaptureAsync("list1", _config);

        capture.Files.Should().Be(1);
        File.Exists(Path.Combine(Store().DirectoryFor("list1"), "Mod_b.xml")).Should().BeFalse();
    }

    /// <summary>A snapshot directory is a folder anything could drop a file into.</summary>
    [Fact]
    public async Task Restore_refuses_a_game_owned_file_smuggled_into_the_snapshot()
    {
        GivenConfigFile("Mod_123_Thing.xml", "<settings />");
        await Store().CaptureAsync("list1", _config);

        File.WriteAllText(
            Path.Combine(Store().DirectoryFor("list1"), "Prefs.xml"), "<prefs>640x480</prefs>");
        GivenConfigFile("Prefs.xml", "<prefs>1920x1080</prefs>");

        await Store().RestoreAsync("list1", _config);

        ConfigText("Prefs.xml").Should().Contain("1920x1080", "checked on the way out too");
    }

    /// <summary>
    /// The capture used to clear its target before writing, so a failure part way through
    /// destroyed the previous good capture and left a partial one that would later be
    /// restored as if whole. Writing first and pruning only on success leaves a superset
    /// on failure, which is recoverable.
    /// </summary>
    [Fact]
    public async Task A_capture_that_fails_part_way_does_not_destroy_the_previous_one()
    {
        GivenConfigFile("Mod_a.xml", "good-a");
        GivenConfigFile("Mod_b.xml", "good-b");
        await Store().CaptureAsync("list1", _config);

        var target = Store().DirectoryFor("list1");

        // The FIRST file alphabetically is locked, so the capture throws before writing
        // anything. That is what discriminates: clear-then-fill leaves the target empty,
        // write-then-prune leaves the previous capture untouched. Locking a later file
        // would not tell the two apart, because the earlier ones get rewritten either way.
        using (var _ = File.Open(Path.Combine(_config, "Mod_a.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { await Store().CaptureAsync("list1", _config); }
            catch (IOException) { /* the point of the test */ }
        }

        File.Exists(Path.Combine(target, "Mod_a.xml")).Should().BeTrue(
            "the previous capture must survive a failed one");
        File.Exists(Path.Combine(target, "Mod_b.xml")).Should().BeTrue();
        File.ReadAllText(Path.Combine(target, "Mod_a.xml")).Should().Be("good-a");
    }

    [Fact]
    public async Task Restoring_a_list_that_never_captured_is_not_an_error()
    {
        (await Store().RestoreAsync("never-captured", _config)).Should().Be(0);
    }

    [Fact]
    public async Task Stored_reports_what_a_list_holds()
    {
        GivenConfigFile("Mod_a.xml", new string('x', 100));
        await Store().CaptureAsync("list1", _config);

        var stored = Store().Stored("list1");
        stored.Files.Should().Be(1);
        stored.Bytes.Should().Be(100);
        Store().Stored("other").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Forget_drops_a_deleted_lists_snapshot()
    {
        GivenConfigFile("Mod_a.xml", "a");
        await Store().CaptureAsync("list1", _config);

        Store().Forget("list1");

        Store().Stored("list1").IsEmpty.Should().BeTrue();
    }
}
