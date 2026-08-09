using RimManager.Core.Abstractions;

namespace RimManager.Core.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IHttpFetcher"/> double: records every request and returns
/// canned bodies, so network-backed clients can be tested with zero I/O — the
/// network analogue of <c>InMemoryFileSystem</c>.
/// </summary>
internal sealed class FakeHttpFetcher : IHttpFetcher
{
    public List<(string Url, IReadOnlyDictionary<string, string> Form)> PostCalls { get; } = [];

    public List<string> GetCalls { get; } = [];

    /// <summary>Computes the POST response body from (url, form). Defaults to empty JSON object.</summary>
    public Func<string, IReadOnlyDictionary<string, string>, string> PostResponder { get; set; } =
        (_, _) => "{}";

    /// <summary>Text responses. Null → the fetcher throws a 404, like the real one.</summary>
    public Func<string, string?> GetResponder { get; set; } = _ => "";

    public Task<string> PostFormAsync(
        string url, IReadOnlyDictionary<string, string> form, CancellationToken ct = default)
    {
        PostCalls.Add((url, form));
        return Task.FromResult(PostResponder(url, form));
    }

    public Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        GetCalls.Add(url);
        var body = GetResponder(url);
        return body is null
            ? Task.FromException<string>(new HttpFetchException(url, 404, "not found"))
            : Task.FromResult(body);
    }

    /// <summary>Bytes responses (the gzipped UseThisInstead payload). Null → 404.</summary>
    public Func<string, byte[]?> BytesResponder { get; set; } = _ => [];

    public Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        GetCalls.Add(url);
        var body = BytesResponder(url);
        return body is null
            ? Task.FromException<byte[]>(new HttpFetchException(url, 404, "not found"))
            : Task.FromResult(body);
    }
}
