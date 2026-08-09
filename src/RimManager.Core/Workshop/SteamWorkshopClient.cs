using System.Collections.Immutable;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;

namespace RimManager.Core.Workshop;

/// <summary>
/// Reads Steam Workshop metadata through the keyless
/// <c>ISteamRemoteStorage/GetPublishedFileDetails/v1/</c> endpoint. Pure
/// orchestration over an <see cref="IHttpFetcher"/> and
/// <see cref="WorkshopMetadataParser"/>: it builds Steam's indexed form body,
/// batches large id sets under the per-call cap, and stitches the pages back
/// together — all unit-testable with a fake fetcher, no network required.
/// </summary>
/// <remarks>
/// The endpoint needs no Web API key (unlike the <c>IPublishedFileService</c>
/// family). RimWorld's app id is <c>294100</c>; callers that care can filter on
/// <see cref="WorkshopItem.ConsumerAppId"/>. Steam does not guarantee response
/// order matches request order, so results are keyed by id, not position.
/// </remarks>
public sealed class SteamWorkshopClient(IHttpFetcher fetcher)
{
    internal const string PublishedFileDetailsUrl =
        "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    internal const string CollectionDetailsUrl =
        "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";

    /// <summary>RimWorld's Steam application id.</summary>
    public const int RimWorldAppId = 294100;

    /// <summary>
    /// How many ids to request per call. Steam accepts large batches, but chunking
    /// keeps any one request (and its failure blast radius) bounded.
    /// </summary>
    private const int BatchSize = 100;

    private readonly IHttpFetcher _fetcher = fetcher;

    /// <summary>
    /// Fetches metadata for the given Workshop ids. Ids that Steam can't resolve come
    /// back as <see cref="WorkshopItemResult.NotFound"/> entries rather than being
    /// dropped. Duplicate and blank ids are removed before the request.
    /// </summary>
    public async Task<ImmutableArray<WorkshopItem>> GetPublishedFileDetailsAsync(
        IEnumerable<string> publishedFileIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(publishedFileIds);

        var ids = publishedFileIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0) return [];

        var all = ImmutableArray.CreateBuilder<WorkshopItem>(ids.Length);
        for (var offset = 0; offset < ids.Length; offset += BatchSize)
        {
            var batch = ids.AsSpan(offset, Math.Min(BatchSize, ids.Length - offset)).ToArray();
            var body = await _fetcher.PostFormAsync(PublishedFileDetailsUrl, BuildForm(batch), ct)
                .ConfigureAwait(false);
            all.AddRange(WorkshopMetadataParser.Parse(body));
        }

        return all.ToImmutable();
    }

    /// <summary>
    /// Convenience keyed lookup: fetches details for the ids and returns a map from
    /// id to item. Missing/unresolved ids are still present (as not-found entries).
    /// </summary>
    public async Task<ImmutableDictionary<string, WorkshopItem>> GetByIdAsync(
        IEnumerable<string> publishedFileIds,
        CancellationToken ct = default)
    {
        var items = await GetPublishedFileDetailsAsync(publishedFileIds, ct).ConfigureAwait(false);
        var map = ImmutableDictionary.CreateBuilder<string, WorkshopItem>(StringComparer.Ordinal);
        foreach (var item in items) map[item.PublishedFileId] = item;
        return map.ToImmutable();
    }

    /// <summary>
    /// Resolves a Workshop collection to its members via <c>GetCollectionDetails</c>.
    /// Returns <c>null</c> if Steam doesn't resolve the id (deleted collection, or an
    /// id that is actually a plain item, not a collection).
    /// </summary>
    public async Task<WorkshopCollection?> GetCollectionAsync(string collectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["collectioncount"] = "1",
            ["publishedfileids[0]"] = collectionId.Trim(),
        };

        var body = await _fetcher.PostFormAsync(CollectionDetailsUrl, form, ct).ConfigureAwait(false);
        var collections = CollectionDetailsParser.Parse(body);
        return collections.FirstOrDefault(c => c.CollectionId == collectionId.Trim())
            ?? collections.FirstOrDefault();
    }

    /// <summary>
    /// Builds Steam's indexed form: <c>itemcount=N</c> plus
    /// <c>publishedfileids[0..N-1]</c>. The fetcher handles url-encoding.
    /// </summary>
    private static Dictionary<string, string> BuildForm(IReadOnlyList<string> ids)
    {
        var form = new Dictionary<string, string>(ids.Count + 1, StringComparer.Ordinal)
        {
            ["itemcount"] = ids.Count.ToString(),
        };
        for (var i = 0; i < ids.Count; i++)
        {
            form[$"publishedfileids[{i}]"] = ids[i];
        }

        return form;
    }
}
