using System.Collections.Immutable;
using System.Text.Json;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.Core.Parsing;

/// <summary>
/// Parses a RimSort-format <c>communityRules.json</c> into <see cref="LoadOrderRules"/>.
/// Tolerant of missing sections and of comments being either a string or an array
/// of strings. The same parser is used for user-rule files (same schema).
/// </summary>
/// <remarks>
/// Phase 2 loads this from a local snapshot; live fetch/sync of the community DB is
/// Phase 6. Shape: <c>{ "rules": { "&lt;packageId&gt;": { "loadAfter": { "&lt;id&gt;":
/// { "comment": [...] } }, "loadBottom": { "value": true } } } }</c>.
/// </remarks>
public static class CommunityRulesParser
{
    public static LoadOrderRules Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Object)
        {
            return LoadOrderRules.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<ModId, ModRules>();
        foreach (var modEntry in rules.EnumerateObject())
        {
            if (!ModId.TryFrom(modEntry.Name, out var id)) continue;
            builder[id] = ParseModRules(modEntry.Value);
        }

        return new LoadOrderRules(builder.ToImmutable());
    }

    private static ModRules ParseModRules(JsonElement element)
    {
        return new ModRules
        {
            LoadAfter = ParseRefs(element, "loadAfter"),
            LoadBefore = ParseRefs(element, "loadBefore"),
            LoadTop = ParseFlag(element, "loadTop", out var topComment),
            LoadTopComment = topComment,
            LoadBottom = ParseFlag(element, "loadBottom", out var bottomComment),
            LoadBottomComment = bottomComment,
        };
    }

    private static ImmutableArray<RuleRef> ParseRefs(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var refs = ImmutableArray.CreateBuilder<RuleRef>();
        foreach (var entry in section.EnumerateObject())
        {
            if (!ModId.TryFrom(entry.Name, out var id)) continue;
            refs.Add(new RuleRef(id, ExtractComment(entry.Value)));
        }

        return refs.ToImmutable();
    }

    private static bool ParseFlag(JsonElement parent, string name, out string? comment)
    {
        comment = null;
        if (!parent.TryGetProperty(name, out var flag) || flag.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        comment = ExtractComment(flag);
        return flag.TryGetProperty("value", out var v)
            && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            && v.GetBoolean();
    }

    /// <summary>Comment may be a plain string or an array of strings; both flatten to one string.</summary>
    private static string? ExtractComment(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("comment", out var comment))
        {
            return null;
        }

        return comment.ValueKind switch
        {
            JsonValueKind.String => comment.GetString(),
            JsonValueKind.Array => string.Join(" ", comment.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())),
            _ => null,
        } is { Length: > 0 } s ? s : null;
    }
}
