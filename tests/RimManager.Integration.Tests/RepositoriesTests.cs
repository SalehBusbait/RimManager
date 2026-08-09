using FluentAssertions;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Storage;
using RimManager.Storage.Repositories;
using Xunit;

namespace RimManager.Integration.Tests;

public sealed class RepositoriesTests : IDisposable
{
    private readonly string _dir;
    private readonly MutableClock _clock = new(DateTimeOffset.Parse("2026-07-24T00:00:00Z"));
    private readonly PhysicalFileSystem _fs;

    public RepositoriesTests()
    {
        _fs = new PhysicalFileSystem(_clock);
        _dir = Path.Combine(Path.GetTempPath(), "rimmanager-repo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    // --- install paths ------------------------------------------------------

    [Fact]
    public async Task Install_paths_repository_round_trips()
    {
        var repo = new InstallPathsRepository(_fs, _dir);
        repo.Load().Should().BeNull("nothing has been saved yet — that is the first-run signal");

        await repo.SaveAsync(new InstallPaths { GameDir = "/g", ConfigDir = "/c" });

        var loaded = repo.Load();
        loaded!.GameDir.Should().Be("/g");
        loaded.ConfigDir.Should().Be("/c");
    }

    /// <summary>
    /// Backups are bounded. Constraint #2 requires one before every overwrite; it does not
    /// require keeping every one forever — and the surfaces that save on every edit make
    /// that distinction expensive. A real install accumulated 55 tags.json backups in a
    /// few days before this existed.
    /// </summary>
    [Fact]
    public async Task Backups_are_pruned_to_the_last_twenty()
    {
        var file = Path.Combine(_dir, "churn.json");

        for (var i = 0; i < PhysicalFileSystem.BackupsKept + 15; i++)
        {
            _clock.UtcNow = _clock.UtcNow.AddSeconds(1);   // one stamp per write
            await _fs.AtomicWriteAsync(file, System.Text.Encoding.UTF8.GetBytes($"{{\"n\":{i}}}"));
        }

        var backups = Directory.GetFiles(_dir, "churn.json.*.bak");

        backups.Should().HaveCount(PhysicalFileSystem.BackupsKept);
        File.ReadAllText(file).Should().Contain($"{PhysicalFileSystem.BackupsKept + 14}",
            "pruning is housekeeping and must never disturb the file itself");
    }

    /// <summary>The ones kept are the NEWEST — the oldest are what nobody needs.</summary>
    [Fact]
    public async Task The_backups_that_survive_are_the_newest_ones()
    {
        var file = Path.Combine(_dir, "churn.json");

        for (var i = 0; i < PhysicalFileSystem.BackupsKept + 5; i++)
        {
            _clock.UtcNow = _clock.UtcNow.AddSeconds(1);
            await _fs.AtomicWriteAsync(file, System.Text.Encoding.UTF8.GetBytes($"v{i}"));
        }

        var newest = Directory.GetFiles(_dir, "churn.json.*.bak").Order(StringComparer.Ordinal).ToList();

        // 25 writes make 24 backups — the FIRST write has nothing to back up — so four
        // are pruned and the oldest survivor holds v4.
        newest.Should().HaveCount(PhysicalFileSystem.BackupsKept);
        File.ReadAllText(newest[0]).Should().Be("v4");
    }

    /// <summary>A write that asks for no backup writes none, and prunes nothing.</summary>
    [Fact]
    public async Task Opting_out_of_backups_leaves_none_behind()
    {
        var file = Path.Combine(_dir, "prefs.json");

        for (var i = 0; i < 5; i++)
        {
            await _fs.AtomicWriteAsync(file, System.Text.Encoding.UTF8.GetBytes($"v{i}"), backup: false);
        }

        Directory.GetFiles(_dir, "prefs.json.*.bak").Should().BeEmpty();
    }

    // --- metadata -----------------------------------------------------------

    [Fact]
    public async Task Metadata_repository_round_trips_and_removes_empty()
    {
        var repo = new MetadataRepository(_fs, _dir);
        var id = ModId.From("Brrainz.Harmony");

        await repo.SetMetadataAsync(id, new ModMetadata { Favorite = true, Note = "essential", TagIds = ["framework"] });
        var loaded = repo.MetadataFor(id);
        loaded.Favorite.Should().BeTrue();
        loaded.Note.Should().Be("essential");
        loaded.TagIds.Should().Equal("framework");

        // Setting empty metadata removes the entry.
        await repo.SetMetadataAsync(id, ModMetadata.Empty);
        repo.LoadModMetadata().Entries.Should().NotContainKey(id.Value);
    }

    [Fact]
    public async Task Tags_and_categories_round_trip()
    {
        var repo = new MetadataRepository(_fs, _dir);
        await repo.SaveTagsAsync(new TagSet([new Tag { Id = "t1", Name = "Framework", PaletteIndex = Palette.Violet }]));
        await repo.SaveCategoriesAsync(new CategorySet([new Category("c1", "Core"), new Category("c2", "QoL", "c1")]));

        repo.LoadTags().Tags.Should().ContainSingle(t => t.Name == "Framework" && t.PaletteIndex == Palette.Violet);
        repo.LoadCategories().Categories.Should().Contain(c => c.Id == "c2" && c.ParentId == "c1");
    }
}
