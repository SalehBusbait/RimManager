using System.Collections.Immutable;
using System.Text.Json;
using RimManager.Core.Domain;

namespace RimManager.Core.Workshop;

/// <summary>
/// Parses a Steam <c>ISteamRemoteStorage/GetCollectionDetails/v1/</c> response into
/// <see cref="WorkshopCollection"/>s. Pure and tolerant, like the other Workshop
/// parsers. Members are returned in Steam's <c>sortorder</c>, which is the order the
/// collection author intended.
/// </summary>
/// <remarks>
/// Shape:
/// <code>
/// { "response": { "result": 1, "resultcount": 1, "collectiondetails": [
///     { "publishedfileid": "123", "result": 1, "children": [
///         { "publishedfileid": "818773962", "sortorder": 0, "filetype": 0 } ] } ] } }
/// </code>
/// A collection id Steam can't resolve comes back with <c>result: 9</c> and no
/// children — surfaced as a <see cref="WorkshopItemResult.NotFound"/> collection.
/// </remarks>
public static class CollectionDetailsParser
{
    public static ImmutableArray<WorkshopCollection> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("response", out var response)
            || response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("collectiondetails", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var collections = ImmutableArray.CreateBuilder<WorkshopCollection>(details.GetArrayLength());
        foreach (var detail in details.EnumerateArray())
        {
            if (ParseCollection(detail) is { } collection) collections.Add(collection);
        }

        return collections.ToImmutable();
    }

    private static WorkshopCollection? ParseCollection(JsonElement detail)
    {
        if (detail.ValueKind != JsonValueKind.Object
            || !detail.TryGetProperty("publishedfileid", out var idElem)
            || idElem.ValueKind != JsonValueKind.String
            || idElem.GetString() is not { Length: > 0 } id)
        {
            return null;
        }

        var result = MapResult(detail);
        if (result != WorkshopItemResult.Ok)
        {
            return new WorkshopCollection { CollectionId = id, Result = result };
        }

        return new WorkshopCollection
        {
            CollectionId = id,
            Result = result,
            MemberIds = ParseMembers(detail),
        };
    }

    private static WorkshopItemResult MapResult(JsonElement detail)
    {
        if (!detail.TryGetProperty("result", out var r) || r.ValueKind != JsonValueKind.Number
            || !r.TryGetInt32(out var code))
        {
            return WorkshopItemResult.Ok; // result omitted on success in some responses
        }

        return code switch
        {
            1 => WorkshopItemResult.Ok,
            9 => WorkshopItemResult.NotFound,
            _ => WorkshopItemResult.Other,
        };
    }

    private static ImmutableArray<string> ParseMembers(JsonElement detail)
    {
        if (!detail.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        // Preserve Steam's sortorder; fall back to response order when absent.
        var ordered = new List<(int Sort, int Index, string Id)>();
        var index = 0;
        foreach (var child in children.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.Object
                || !child.TryGetProperty("publishedfileid", out var idElem)
                || idElem.ValueKind != JsonValueKind.String
                || idElem.GetString() is not { Length: > 0 } id)
            {
                index++;
                continue;
            }

            var sort = child.TryGetProperty("sortorder", out var s) && s.ValueKind == JsonValueKind.Number
                       && s.TryGetInt32(out var so)
                ? so
                : index;
            ordered.Add((sort, index, id));
            index++;
        }

        return ordered
            .OrderBy(m => m.Sort)
            .ThenBy(m => m.Index)
            .Select(m => m.Id)
            .ToImmutableArray();
    }
}
