using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Storage;
using RimManager.Storage.Persistence;
using Xunit;

namespace RimManager.Integration.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _dir;

    public PersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rimmanager-persist-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }

    /// <summary>These tests are about the ENVELOPE — any serialisable record does. A
    /// local one keeps them decoupled from the domain, whose types come and go.</summary>
    private sealed record Doc
    {
        public required string Name { get; init; }
    }

    private static Doc Sample(string name) => new() { Name = name };

    [Fact]
    public async Task Round_trips_through_a_versioned_envelope()
    {
        var store = new JsonDocumentStore<Doc>(new PhysicalFileSystem());
        var path = Path.Combine(_dir, "doc.json");

        await store.SaveAsync(path, Sample("Alpha"));

        File.ReadAllText(path).Should().Contain("\"schemaVersion\": 1");
        store.Load(path)!.Name.Should().Be("Alpha");
    }

    [Fact]
    public void Missing_file_loads_as_null()
    {
        new JsonDocumentStore<Doc>(new PhysicalFileSystem())
            .Load(Path.Combine(_dir, "nope.json")).Should().BeNull();
    }

    [Fact]
    public async Task Recovers_from_a_backup_when_the_primary_is_corrupt()
    {
        var store = new JsonDocumentStore<Doc>(new PhysicalFileSystem());
        var path = Path.Combine(_dir, "doc.json");

        await store.SaveAsync(path, Sample("Good"));          // create the file
        File.Copy(path, path + ".20260724T000000Z.bak");      // a valid backup exists
        await File.WriteAllTextAsync(path, "{ this is not json");  // corrupt the primary

        store.Load(path)!.Name.Should().Be("Good", "the store must recover the backup, not crash");
    }

    [Fact]
    public async Task Unrecoverable_corruption_throws_rather_than_losing_silently()
    {
        var store = new JsonDocumentStore<Doc>(new PhysicalFileSystem());
        var path = Path.Combine(_dir, "doc.json");
        await File.WriteAllTextAsync(path, "totally broken");

        var act = () => store.Load(path);
        act.Should().Throw<CorruptDocumentException>();
    }

    [Fact]
    public async Task A_newer_schema_version_is_reported_not_treated_as_corruption()
    {
        var store = new JsonDocumentStore<Doc>(new PhysicalFileSystem());
        var path = Path.Combine(_dir, "doc.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 999, "data": { "name": "y" } }""");

        var act = () => store.Load(path);
        act.Should().Throw<SchemaTooNewException>();
    }
}
