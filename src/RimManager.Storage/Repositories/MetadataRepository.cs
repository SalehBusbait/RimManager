using System.Collections.Immutable;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Storage.Persistence;

namespace RimManager.Storage.Repositories;

/// <summary>
/// Identity-keyed organization data (spec §4.3): tag definitions, the category tree,
/// and per-mod metadata (notes/favorite/alias/tags/category), at the app root by
/// default. Each is its own diffable JSON file. All are source-of-truth user data.
/// </summary>
public sealed class MetadataRepository
{
    private readonly string _tagsFile;
    private readonly string _categoriesFile;
    private readonly string _modMetaFile;
    private readonly JsonDocumentStore<TagSet> _tags;
    private readonly JsonDocumentStore<CategorySet> _categories;
    private readonly JsonDocumentStore<ModMetadataSet> _modMeta;

    public MetadataRepository(IFileSystem fs, string? root = null)
    {
        root ??= AppPaths.Root;
        _tagsFile = Path.Combine(root, "tags.json");
        _categoriesFile = Path.Combine(root, "categories.json");
        _modMetaFile = Path.Combine(root, "modmeta.json");
        // v1 -> v2 moves tag colours from hex strings to a palette index (#6).
        _tags = new JsonDocumentStore<TagSet>(fs, [PaletteMigrations.TagsV1ToV2]);
        _categories = new JsonDocumentStore<CategorySet>(fs);
        _modMeta = new JsonDocumentStore<ModMetadataSet>(fs);
    }

    /// <summary>
    /// Where the per-mod metadata actually lives, for Settings ▸ Tags &amp; metadata to
    /// report. Exposed rather than reconstructed by the caller: rebuilding this path
    /// elsewhere is how the Settings page came to name <c>modMetadata.json</c>, a file
    /// that does not exist, and report its size as 0 B.
    /// </summary>
    public string ModMetadataPath => _modMetaFile;

    public TagSet LoadTags() => _tags.Load(_tagsFile) ?? TagSet.Empty;
    public Task SaveTagsAsync(TagSet tags, CancellationToken ct = default) => _tags.SaveAsync(_tagsFile, tags, ct: ct);

    public CategorySet LoadCategories() => _categories.Load(_categoriesFile) ?? CategorySet.Empty;
    public Task SaveCategoriesAsync(CategorySet categories, CancellationToken ct = default) =>
        _categories.SaveAsync(_categoriesFile, categories, ct: ct);

    public ModMetadataSet LoadModMetadata() => _modMeta.Load(_modMetaFile) ?? ModMetadataSet.Empty;
    public Task SaveModMetadataAsync(ModMetadataSet meta, CancellationToken ct = default) =>
        _modMeta.SaveAsync(_modMetaFile, meta, ct: ct);

    /// <summary>Returns the metadata for one mod (keyed by canonical packageId), or empty.</summary>
    public ModMetadata MetadataFor(ModId packageId) =>
        LoadModMetadata().Entries.GetValueOrDefault(packageId.Value, ModMetadata.Empty);

    /// <summary>Sets (or clears, when empty) the metadata for one mod and persists.</summary>
    public async Task SetMetadataAsync(ModId packageId, ModMetadata metadata, CancellationToken ct = default)
    {
        var current = LoadModMetadata().Entries;
        var updated = metadata.IsEmpty
            ? current.Remove(packageId.Value)
            : current.SetItem(packageId.Value, metadata);
        await SaveModMetadataAsync(new ModMetadataSet(updated), ct).ConfigureAwait(false);
    }
}
