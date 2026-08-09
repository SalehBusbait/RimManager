using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;
using RimManager.Core.Tests.Fakes;
using RimManager.Core.Writing;
using Xunit;

namespace RimManager.Core.Tests.Writing;

public sealed class ApplyServiceTests
{
    private static readonly FixedClock Clock = new(DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

    private static ModsConfig Config(params string[] active) =>
        new("1.6", [.. active.Select(ModId.From)], [ModId.From("ludeon.rimworld.royalty")]);

    // --- O5 · backups move out of the game's config folder ---------------------

    [Fact]
    public async Task The_backup_lands_in_the_folder_it_is_given()
    {
        var fs = new InMemoryFileSystem(Clock);
        fs.AddFile("/config/ModsConfig.xml", "<ModsConfigData />");
        var service = new ApplyService(fs, new FakeGameDetector(running: false), Clock);

        var result = await service.ApplyAsync(
            "/config/ModsConfig.xml", Config("a.b"), backupDirectory: "/rimmanager/backups");

        // Asserted on the parts, not on one string: Path.Combine uses the platform
        // separator, so pinning "/rimmanager/backups/…" would be a test that only passes
        // off Windows while the behaviour is identical on both.
        System.IO.Path.GetFileName(result.BackupPath!)
            .Should().Be("ModsConfig.20260724T000000Z.xml", "the stamp is UTC and sorts");
        result.BackupPath.Should().Contain("backups");
        fs.FileExists(result.BackupPath!).Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_is_left_beside_ModsConfig_when_a_backup_folder_is_given()
    {
        // The whole point of O5: RimWorld's config folder is the game's, and our copies
        // sitting in it read as more config to every other tool that looks there.
        var fs = new InMemoryFileSystem(Clock);
        fs.AddFile("/config/ModsConfig.xml", "<ModsConfigData />");
        var service = new ApplyService(fs, new FakeGameDetector(running: false), Clock);

        await service.ApplyAsync(
            "/config/ModsConfig.xml", Config("a.b"), backupDirectory: "/rimmanager/backups");

        fs.EnumerateEntries("/config")
            .Select(e => System.IO.Path.GetFileName(e.FullPath))
            .Should().ContainSingle().Which.Should().Be("ModsConfig.xml");
    }

    [Fact]
    public async Task A_first_apply_with_no_existing_config_backs_nothing_up()
    {
        var fs = new InMemoryFileSystem(Clock);
        var service = new ApplyService(fs, new FakeGameDetector(running: false), Clock);

        var result = await service.ApplyAsync(
            "/config/ModsConfig.xml", Config("a.b"), backupDirectory: "/rimmanager/backups");

        result.Written.Should().BeTrue();
        result.BackupPath.Should().BeNull("there was no previous file to copy");
        result.Message.Should().NotContain("backup");
    }

    [Fact]
    public async Task Without_a_folder_the_old_sibling_behaviour_is_unchanged()
    {
        // The CLI's other entry points and the tests below still call the two-argument
        // form; it must keep meaning what it always meant.
        var fs = new InMemoryFileSystem(Clock);
        fs.AddFile("/config/ModsConfig.xml", "<ModsConfigData />");
        var service = new ApplyService(fs, new FakeGameDetector(running: false), Clock);

        var result = await service.ApplyAsync("/config/ModsConfig.xml", Config("a.b"));

        result.BackupPath.Should().NotBeNull();
        result.BackupPath.Should().StartWith("/config/");
    }

    [Fact]
    public async Task Only_the_newest_twenty_backups_are_kept()
    {
        var fs = new InMemoryFileSystem(Clock);
        for (var i = 0; i < 25; i++)
            fs.AddFile($"/rimmanager/backups/ModsConfig.202607{i + 1:00}T000000Z.xml", "old");
        fs.AddFile("/config/ModsConfig.xml", "<ModsConfigData />");

        var service = new ApplyService(fs, new FakeGameDetector(running: false), Clock);
        await service.ApplyAsync(
            "/config/ModsConfig.xml", Config("a.b"), backupDirectory: "/rimmanager/backups");

        fs.EnumerateEntries("/rimmanager/backups").Count()
            .Should().Be(ApplyService.BackupsKept);
    }

    [Fact]
    public async Task Refuses_to_write_while_the_game_is_running()
    {
        var fs = new InMemoryFileSystem(Clock);
        var service = new ApplyService(fs, new FakeGameDetector(running: true));

        var result = await service.ApplyAsync("/config/ModsConfig.xml", Config("a.b"));

        result.Written.Should().BeFalse();
        result.Message.Should().Contain("running");
        fs.FileExists("/config/ModsConfig.xml").Should().BeFalse("nothing should be written");
    }

    [Fact]
    public async Task Writes_when_the_game_is_not_running()
    {
        var fs = new InMemoryFileSystem(Clock);
        var service = new ApplyService(fs, new FakeGameDetector(running: false));

        var result = await service.ApplyAsync("/config/ModsConfig.xml", Config("brrainz.harmony", "ludeon.rimworld"));

        result.Written.Should().BeTrue();
        var written = fs.ReadAllText("/config/ModsConfig.xml");
        ModsConfigParser.Parse(written).ActiveMods
            .Should().Equal(ModId.From("brrainz.harmony"), ModId.From("ludeon.rimworld"));
    }

    [Fact]
    public async Task Backs_up_an_existing_file()
    {
        var fs = new InMemoryFileSystem(Clock);
        fs.AddFile("/config/ModsConfig.xml", "old contents");
        var service = new ApplyService(fs, new FakeGameDetector(running: false));

        var result = await service.ApplyAsync("/config/ModsConfig.xml", Config("a.b"));

        result.BackupPath.Should().NotBeNull();
        fs.ReadAllText(result.BackupPath!).Should().Be("old contents");
    }

    [Fact]
    public void WithActiveOrder_preserves_version_and_known_expansions()
    {
        var existing = Config("a.b", "c.d");
        var updated = ApplyService.WithActiveOrder(existing, [ModId.From("c.d"), ModId.From("a.b")]);

        updated.Version.Should().Be(existing.Version);
        updated.KnownExpansions.Should().Equal(existing.KnownExpansions);
        updated.ActiveMods.Should().Equal(ModId.From("c.d"), ModId.From("a.b"));
    }
}
