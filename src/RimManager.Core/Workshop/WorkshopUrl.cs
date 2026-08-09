using System.Diagnostics.CodeAnalysis;

namespace RimManager.Core.Workshop;

/// <summary>
/// Extracts a Steam published-file id (a collection or item) from the things a user
/// might paste: a full Workshop URL, or a bare numeric id. Pure string work — no
/// <c>System.Web</c> / <c>Uri</c> dependency, so it behaves identically everywhere.
/// </summary>
/// <remarks>
/// Accepts, e.g.:
/// <list type="bullet">
///   <item><c>https://steamcommunity.com/sharedfiles/filedetails/?id=12345</c></item>
///   <item><c>https://steamcommunity.com/workshop/filedetails/?id=12345&amp;searchtext=x</c></item>
///   <item><c>steam://url/CommunityFilePage/12345</c></item>
///   <item><c>12345</c></item>
/// </list>
/// A published-file id is a run of ASCII digits; that's all we validate.
/// </remarks>
public static class WorkshopUrl
{
    public static bool TryGetId(string? input, [NotNullWhen(true)] out string? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();

        // Bare id.
        if (IsAllDigits(s))
        {
            id = s;
            return true;
        }

        // Query form: ...?id=NNN or ...&id=NNN
        if (TryFromQuery(s, out id)) return true;

        // steam://url/CommunityFilePage/NNN or any trailing /NNN path segment.
        return TryFromTrailingSegment(s, out id);
    }

    private static bool TryFromQuery(string s, [NotNullWhen(true)] out string? id)
    {
        id = null;
        var q = s.IndexOf('?');
        if (q < 0) return false;

        // Drop any #fragment so it doesn't cling to the last query value.
        var query = s[(q + 1)..].Split('#', 2)[0];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (!pair[..eq].Equals("id", StringComparison.OrdinalIgnoreCase)) continue;

            var value = pair[(eq + 1)..];
            if (IsAllDigits(value)) { id = value; return true; }
        }

        return false;
    }

    private static bool TryFromTrailingSegment(string s, [NotNullWhen(true)] out string? id)
    {
        id = null;
        // Strip a query/fragment, then take the last non-empty path segment.
        var path = s.Split('?', '#')[0].TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0) return false;

        var segment = path[(lastSlash + 1)..];
        if (!IsAllDigits(segment)) return false;

        id = segment;
        return true;
    }

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
        {
            if (c is < '0' or > '9') return false;
        }

        return true;
    }
}
