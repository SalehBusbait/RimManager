namespace RimManager.Core.Abstractions;

/// <summary>
/// The single network I/O seam, in the spirit of <see cref="IFileSystem"/>:
/// <c>RimManager.Core</c> never opens a socket directly, so every network-backed
/// client (Steam Workshop metadata, GitHub releases, update-checking) is a pure
/// composition of this seam plus a string parser — and therefore unit-testable
/// with an in-memory double that returns canned response bodies.
/// </summary>
/// <remarks>
/// Kept to the two verbs Phase 6 needs: a form-encoded POST (Steam's keyless
/// <c>GetPublishedFileDetails</c>/<c>GetCollectionDetails</c> are POSTs) and a
/// plain GET (GitHub releases, raw JSON). Implementations own transport concerns
/// — base address, User-Agent, timeouts, TLS, connection reuse — none of which
/// belong in the domain. A non-success HTTP status should throw
/// <see cref="HttpFetchException"/> so callers can distinguish "network failed"
/// from "Steam said this id doesn't exist" (which is a 200 with per-item result 9).
/// </remarks>
public interface IHttpFetcher
{
    /// <summary>
    /// POSTs <paramref name="form"/> as <c>application/x-www-form-urlencoded</c> to
    /// <paramref name="url"/> and returns the response body as text.
    /// </summary>
    Task<string> PostFormAsync(
        string url,
        IReadOnlyDictionary<string, string> form,
        CancellationToken ct = default);

    /// <summary>GETs <paramref name="url"/> and returns the response body as text.</summary>
    Task<string> GetStringAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// GETs <paramref name="url"/> and returns the raw response body. Added for the
    /// first binary payload (N7: UseThisInstead ships its database gzipped); text
    /// callers should stay on <see cref="GetStringAsync"/>.
    /// </summary>
    Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default);
}

/// <summary>Thrown when a request completes transport-wise but the server returns a non-success status.</summary>
public sealed class HttpFetchException(string url, int statusCode, string message)
    : Exception($"{url} → HTTP {statusCode}: {message}")
{
    public string Url { get; } = url;

    public int StatusCode { get; } = statusCode;
}
