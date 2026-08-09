using System.Collections.Immutable;
using System.Text.Json;
using RimManager.Core.Domain;

namespace RimManager.Core.Workshop;

/// <summary>
/// Parses a Steam <c>ISteamRemoteStorage/GetPublishedFileDetails/v1/</c> response
/// body into <see cref="WorkshopItem"/>s. Pure and tolerant in the same spirit as
/// <c>CommunityRulesParser</c>: unknown fields are ignored, and Steam's quirks
/// (numeric <c>file_size</c> shipped as a string, times as unix seconds, an item's
/// own per-entry <c>result</c>) are normalized here so the domain type stays clean.
/// </summary>
/// <remarks>
/// Response shape:
/// <code>
/// { "response": { "result": 1, "resultcount": N, "publishedfiledetails": [
///     { "publishedfileid": "818773962", "result": 1, "creator": "76561198...",
///       "consumer_app_id": 294100, "file_size": "1234567", "title": "HugsLib",
///       "description": "...", "time_created": 1478..., "time_updated": 1600...,
///       "tags": [ { "tag": "Mod" } ], "children": [ { "publishedfileid": "..." } ] } ] } }
/// </code>
/// An id that Steam can't resolve still appears in <c>publishedfiledetails</c>, but
/// with <c>result: 9</c> and no other fields — surfaced as
/// <see cref="WorkshopItemResult.NotFound"/> rather than dropped, so callers can tell
/// "deleted/hidden" from "never asked".
/// </remarks>
public static class WorkshopMetadataParser
{
    public static ImmutableArray<WorkshopItem> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("response", out var response)
            || response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("publishedfiledetails", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = ImmutableArray.CreateBuilder<WorkshopItem>(details.GetArrayLength());
        foreach (var detail in details.EnumerateArray())
        {
            if (ParseItem(detail) is { } item) items.Add(item);
        }

        return items.ToImmutable();
    }

    private static WorkshopItem? ParseItem(JsonElement detail)
    {
        if (detail.ValueKind != JsonValueKind.Object) return null;

        var id = GetString(detail, "publishedfileid");
        if (string.IsNullOrEmpty(id)) return null;

        var result = MapResult(GetInt(detail, "result"));
        if (result != WorkshopItemResult.Ok)
        {
            // Not-found / other: Steam echoes only the id and result; don't fabricate the rest.
            return new WorkshopItem { PublishedFileId = id, Result = result };
        }

        return new WorkshopItem
        {
            PublishedFileId = id,
            Result = result,
            Title = GetString(detail, "title"),
            Description = GetString(detail, "description"),
            ConsumerAppId = GetInt(detail, "consumer_app_id") ?? 0,
            Creator = GetString(detail, "creator"),
            FileSize = GetInt64String(detail, "file_size"),
            TimeCreatedUtc = GetUnixTime(detail, "time_created"),
            TimeUpdatedUtc = GetUnixTime(detail, "time_updated"),
            Tags = ParseTags(detail),
            Children = ParseChildren(detail),
        };
    }

    private static WorkshopItemResult MapResult(int? result) => result switch
    {
        1 => WorkshopItemResult.Ok,
        9 => WorkshopItemResult.NotFound,
        null => WorkshopItemResult.Ok, // some responses omit per-item result on success
        _ => WorkshopItemResult.Other,
    };

    private static ImmutableArray<string> ParseTags(JsonElement detail)
    {
        if (!detail.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in tags.EnumerateArray())
        {
            var tag = GetString(entry, "tag");
            if (!string.IsNullOrEmpty(tag)) builder.Add(tag);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> ParseChildren(JsonElement detail)
    {
        if (!detail.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in children.EnumerateArray())
        {
            var childId = GetString(entry, "publishedfileid");
            if (!string.IsNullOrEmpty(childId)) builder.Add(childId);
        }

        return builder.ToImmutable();
    }

    // --- scalar helpers: forgiving about string-vs-number, absent, or wrong-kind fields ---

    private static string? GetString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var i)
            ? i
            : null;

    /// <summary>Steam ships <c>file_size</c> as a JSON string; accept a number too, for safety.</summary>
    private static long? GetInt64String(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => long.TryParse(v.GetString(), out var n) ? n : null,
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : null,
            _ => null,
        };
    }

    /// <summary>Times are unix seconds (a JSON number); 0 means "unset" and maps to null.</summary>
    private static DateTimeOffset? GetUnixTime(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number
            || !v.TryGetInt64(out var seconds) || seconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
