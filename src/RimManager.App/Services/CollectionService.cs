using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimManager.App.ViewModels;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.App.Services;

/// <summary>The outcome of resolving a collection URL against the local install.</summary>
/// <param name="Title">
/// The collection's own Workshop title. A collection is itself a published file, so it
/// comes back from the same metadata call the members use — one extra id in a batch we
/// were already making. Without it the import header can only name the numeric id,
/// which is the one place that should say what you just imported.
/// </param>
/// <param name="UpdatedUtc">
/// When the collection itself last changed, from the same metadata entry the title
/// came from. The import wizard (<c>2i</c>-3) prints it as "updated 3 days ago" —
/// which is a real published field, unlike the author name the design shows beside it:
/// Steam returns <c>creator</c> as a SteamID64, and resolving it needs a Web API key.
/// </param>
/// <param name="Offline">
/// The lookup never got an answer, as opposed to getting one it did not like. This
/// resolver reports failure as a RESULT rather than an exception, because the wizard
/// prints it in its own hint line — so connectivity has to ride on the result too.
/// It did not, and the caller's <c>catch</c> for it was unreachable: opening the
/// import wizard while offline and pasting a URL took the strip DOWN, because the
/// try block completed and the success path cleared the offline state. Found by the
/// R9 hand review.
/// </param>
public sealed record CollectionResolution(
    bool Ok, string? CollectionId, string? Title, CollectionReport Report, string? Error,
    DateTimeOffset? UpdatedUtc = null, bool Offline = false);

/// <summary>
/// The App's bridge to Workshop collection import: resolves a collection URL to its
/// members and reconciles them against installed mods. Thin I/O orchestration over the
/// Core-tested <see cref="WorkshopUrl"/>, <see cref="SteamWorkshopClient"/>, and
/// <see cref="CollectionReconciler"/> — the same pipeline as CLI <c>workshop collection</c>.
/// </summary>
public sealed class CollectionService(IHttpFetcher fetcher)
{
    private static readonly CollectionReport Empty = new() { Members = [] };

    public async Task<CollectionResolution> ResolveAsync(
        string url, IReadOnlyList<Mod> installed, CancellationToken ct = default)
    {
        if (!WorkshopUrl.TryGetId(url, out var id))
            return new CollectionResolution(false, null, null, Empty, "Enter a Workshop collection URL or numeric id.");

        var client = new SteamWorkshopClient(fetcher);
        try
        {
            var collection = await client.GetCollectionAsync(id, ct).ConfigureAwait(false);
            if (collection is null || !collection.IsOk || collection.MemberIds.IsDefaultOrEmpty)
            {
                return new CollectionResolution(false, id, null, Empty,
                    "Collection didn't resolve (deleted, private, or an item rather than a collection).");
            }

            // The collection's own id rides along in the same batch: it is a published
            // file like any other, and its title is what the import header shows.
            var metadata = await client
                .GetByIdAsync(collection.MemberIds.Append(id), ct)
                .ConfigureAwait(false);

            var found = metadata.TryGetValue(id, out var self) && self.IsOk ? self : null;
            var report = CollectionReconciler.Reconcile(
                collection.MemberIds, CollectionReconciler.IndexByFileId(installed), metadata);
            return new CollectionResolution(true, id, found?.Title, report, null, found?.TimeUpdatedUtc);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or HttpFetchException)
        {
            // HttpFetchException is a server that answered — that is proof we are
            // online, and must not raise the offline state (2k). The other two are a
            // request that never got an answer.
            var offline = NetworkFailure.IsConnectivity(ex);
            var message = offline
                ? "Could not reach Steam — check your connection and try again."
                : $"Lookup failed: {ex.Message}";

            return new CollectionResolution(false, id, null, Empty, message, Offline: offline);
        }
    }
}
