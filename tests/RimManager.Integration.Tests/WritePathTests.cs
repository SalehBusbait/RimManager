using FluentAssertions;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;
using RimManager.Core.Writing;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

public sealed class WritePathTests : IDisposable
{
    private readonly string _dir;

    public WritePathTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rimmanager-write-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }

    [SkippableFact]
    public void Writer_reproduces_the_real_mods_config_byte_for_byte()
    {
        var path = Fixtures.ModsConfig();
        Skip.If(path is null, "No ModsConfig.xml fixture.");

        var originalBytes = File.ReadAllBytes(path!);
        var config = ModsConfigParser.Parse(File.ReadAllText(path!));

        var rewritten = ModsConfigWriter.SerializeToBytes(config);

        rewritten.Should().Equal(originalBytes,
            "the writer must reproduce RimWorld's exact formatting so applies produce clean diffs");
    }

    [Fact]
    public async Task Apply_writes_atomically_with_a_backup_on_disk()
    {
        var configPath = Path.Combine(_dir, "ModsConfig.xml");
        var fs = new PhysicalFileSystem();
        var service = new ApplyService(fs, NeverRunningGameDetector.Instance);

        var v1 = new ModsConfig("1.6", [ModId.From("a.b")], []);
        var v2 = new ModsConfig("1.6", [ModId.From("a.b"), ModId.From("c.d")], []);

        var first = await service.ApplyAsync(configPath, v1);
        first.BackupPath.Should().BeNull("nothing existed to back up");

        var second = await service.ApplyAsync(configPath, v2);
        second.BackupPath.Should().NotBeNull();

        ModsConfigParser.Parse(File.ReadAllText(configPath)).ActiveMods
            .Should().Equal(ModId.From("a.b"), ModId.From("c.d"));
        // The backup holds the previous version.
        ModsConfigParser.Parse(File.ReadAllText(second.BackupPath!)).ActiveMods
            .Should().Equal(ModId.From("a.b"));
    }
}
