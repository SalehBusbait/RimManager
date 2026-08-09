using System.Text;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using RimManager.Storage.Repositories;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// The v1 → v2 migration that moves tag and separator colours off hex strings and
/// onto a palette index (design non-negotiable #6).
/// <para>
/// These go through the real repositories against real files on disk, because the
/// thing worth proving is that a document written by the *previous shipped build*
/// still loads — this is the one step in the redesign that rewrites user data.
/// </para>
/// </summary>
public sealed class PaletteMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rimmanager-migrate-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly RimManager.Storage.PhysicalFileSystem _fs = new();

    public PaletteMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private void WriteLegacy(string relativePath, string json)
    {
        var path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    [Fact]
    public void Legacy_tag_colours_migrate_to_palette_indices()
    {
        // Exactly what the pre-migration build wrote: schemaVersion 1, hex colours.
        WriteLegacy("tags.json", """
        {
          "schemaVersion": 1,
          "data": {
            "tags": [
              { "id": "t1", "name": "Framework", "color": "#5B9BD5" },
              { "id": "t2", "name": "Overhaul",  "color": "#9C6ADE" },
              { "id": "t3", "name": "Combat",    "color": "#4CAF50" }
            ]
          }
        }
        """);

        var tags = new MetadataRepository(_fs, _dir).LoadTags().Tags;

        tags.Should().HaveCount(3);
        tags.Single(t => t.Id == "t1").PaletteIndex.Should().Be(Palette.Blue);
        tags.Single(t => t.Id == "t2").PaletteIndex.Should().Be(Palette.Violet);
        tags.Single(t => t.Id == "t3").PaletteIndex.Should().Be(Palette.Green);

        // Names and ids are untouched — only the colour representation changed.
        tags.Select(t => t.Name).Should().BeEquivalentTo(["Framework", "Overhaul", "Combat"]);
    }

    [Fact]
    public void Migrated_tags_default_the_fields_the_old_schema_never_had()
    {
        WriteLegacy("tags.json", """
        { "schemaVersion": 1, "data": { "tags": [ { "id": "t1", "name": "Framework", "color": "#5B9BD5" } ] } }
        """);

        var tag = new MetadataRepository(_fs, _dir).LoadTags().Tags.Single();

        tag.ShowAsStripe.Should().BeTrue("an existing tag should keep showing in the lists");
        tag.AutoAssign.Should().BeEmpty();
    }

    [Fact]
    public void A_tag_with_an_unreadable_colour_still_loads()
    {
        // Losing the whole tag list because one colour was hand-edited would be a far
        // worse outcome than one tag coming back the wrong shade.
        WriteLegacy("tags.json", """
        {
          "schemaVersion": 1,
          "data": {
            "tags": [
              { "id": "t1", "name": "Broken", "color": "puce" },
              { "id": "t2", "name": "Fine",   "color": "#4CAF50" }
            ]
          }
        }
        """);

        var tags = new MetadataRepository(_fs, _dir).LoadTags().Tags;

        tags.Should().HaveCount(2);
        tags.Single(t => t.Id == "t1").PaletteIndex.Should().Be(Palette.Blue);
        tags.Single(t => t.Id == "t2").PaletteIndex.Should().Be(Palette.Green);
    }

    [Fact]
    public async Task A_migrated_document_is_rewritten_at_the_new_schema_version()
    {
        WriteLegacy("tags.json", """
        { "schemaVersion": 1, "data": { "tags": [ { "id": "t1", "name": "Framework", "color": "#5B9BD5" } ] } }
        """);

        var repo = new MetadataRepository(_fs, _dir);
        await repo.SaveTagsAsync(repo.LoadTags());

        var written = File.ReadAllText(Path.Combine(_dir, "tags.json"));
        written.Should().Contain("\"schemaVersion\": 2");
        written.Should().Contain("paletteIndex");
        written.Should().NotContain("#5B9BD5", "the hex representation is gone for good");
    }
}
