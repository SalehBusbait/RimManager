using FluentAssertions;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

public sealed class SqliteModCacheTests : IDisposable
{
    private readonly string _dir;

    public SqliteModCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rimmanager-cache-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }

    private string DbPath => Path.Combine(_dir, "mods.db");

    private static Mod SampleMod() => new()
    {
        PackageId = ModId.From("Brrainz.Harmony"),
        Name = "Harmony",
        Authors = ["Andreas Pardeike"],
        SupportedVersions = ["1.5", "1.6"],
        ModVersion = "2.4.2.0",
        Source = ModSource.Workshop,
        RootPath = "/ws/2009463077",
        Content = ContentFlags.Assemblies,
    };

    private static FileEntry Stat(long ticks, long size) =>
        new("/ws/2009463077/About/About.xml", false, size, new DateTimeOffset(ticks, TimeSpan.Zero));

    [Fact]
    public void Round_trips_a_mod_and_preserves_id_casing()
    {
        var stat = Stat(1000, 500);
        using (var cache = SqliteModCache.Open(DbPath))
        {
            cache.Put(stat.FullPath, stat, SampleMod());
            cache.Flush();
        }

        using var reopened = SqliteModCache.Open(DbPath);
        var got = reopened.TryGet(stat.FullPath, stat);

        got.Should().NotBeNull();
        got!.PackageId.Should().Be(ModId.From("brrainz.harmony"));
        got.PackageId.Display.Should().Be("Brrainz.Harmony", "display casing must survive serialization");
        got.Content.Should().Be(ContentFlags.Assemblies);
        got.SupportedVersions.Should().Equal("1.5", "1.6");
    }

    [Fact]
    public void Stat_mismatch_is_a_miss()
    {
        using var cache = SqliteModCache.Open(DbPath);
        var stat = Stat(1000, 500);
        cache.Put(stat.FullPath, stat, SampleMod());
        cache.Flush();

        cache.TryGet(stat.FullPath, Stat(1000, 999)).Should().BeNull("size changed");
        cache.TryGet(stat.FullPath, Stat(2000, 500)).Should().BeNull("mtime changed");
        cache.TryGet(stat.FullPath, stat).Should().NotBeNull("unchanged stat still hits");
    }
}
