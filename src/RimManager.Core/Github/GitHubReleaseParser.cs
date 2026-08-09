using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace RimManager.Core.Github;

/// <summary>
/// Parses GitHub's <c>/releases</c> JSON (an array) or <c>/releases/latest</c> (a
/// single object) into <see cref="GitHubRelease"/>s. Pure and tolerant, matching the
/// Workshop parsers: unknown fields ignored, missing fields defaulted.
/// </summary>
public static class GitHubReleaseParser
{
    /// <summary>Parses either a releases array or a single release object into a list.</summary>
    public static ImmutableArray<GitHubRelease> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            var builder = ImmutableArray.CreateBuilder<GitHubRelease>(root.GetArrayLength());
            foreach (var element in root.EnumerateArray())
            {
                if (ParseRelease(element) is { } release) builder.Add(release);
            }

            return builder.ToImmutable();
        }

        return ParseRelease(root) is { } single ? [single] : [];
    }

    /// <summary>Parses a single-release response (<c>/releases/latest</c>), or null.</summary>
    public static GitHubRelease? ParseSingle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseRelease(doc.RootElement);
    }

    private static GitHubRelease? ParseRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var tag = GetString(element, "tag_name");
        if (string.IsNullOrEmpty(tag)) return null;

        return new GitHubRelease
        {
            TagName = tag,
            Name = GetString(element, "name"),
            PublishedAtUtc = GetTimestamp(element, "published_at"),
            IsPrerelease = GetBool(element, "prerelease"),
            IsDraft = GetBool(element, "draft"),
            HtmlUrl = GetString(element, "html_url"),
            Assets = ParseAssets(element),
        };
    }

    private static ImmutableArray<GitHubReleaseAsset> ParseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<GitHubReleaseAsset>(assets.GetArrayLength());
        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var url = GetString(asset, "browser_download_url");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;

            builder.Add(new GitHubReleaseAsset
            {
                Name = name,
                DownloadUrl = url,
                Size = GetLong(asset, "size") ?? 0,
                ContentType = GetString(asset, "content_type"),
            });
        }

        return builder.ToImmutable();
    }

    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static long? GetLong(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : null;

    private static DateTimeOffset? GetTimestamp(JsonElement parent, string name)
    {
        var s = GetString(parent, name);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : null;
    }
}
