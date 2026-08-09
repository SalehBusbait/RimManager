using System.Net.Http;
using RimManager.Core.Abstractions;

namespace RimManager.Integrations.Http;

/// <summary>
/// The concrete <see cref="IHttpFetcher"/> — the one place in the app that opens a
/// socket. Lives in <c>RimManager.Integrations</c> (the network edge) so the domain
/// stays transport-free and unit-testable. Not itself unit-tested; exercised by a
/// live <c>[SkippableFact]</c> integration test that skips when offline.
/// </summary>
/// <remarks>
/// Wraps a single reusable <see cref="HttpClient"/> (creating one per request leaks
/// sockets). The default instance owns its client; callers that already manage an
/// <see cref="HttpClient"/> (e.g. the App via <c>IHttpClientFactory</c>) can pass one
/// in, in which case its lifetime is the caller's. Steam and GitHub both reject
/// requests without a User-Agent, so one is always set.
/// </remarks>
public sealed class HttpClientFetcher : IHttpFetcher, IDisposable
{
    private static readonly string DefaultUserAgent =
        $"RimManager/{typeof(HttpClientFetcher).Assembly.GetName().Version?.ToString(2) ?? "1.0"} (+https://github.com/rimmanager)";

    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    /// <summary>Creates a fetcher that owns an <see cref="HttpClient"/> with a sane timeout and User-Agent.</summary>
    public HttpClientFetcher(TimeSpan? timeout = null)
    {
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        _ownsClient = true;
    }

    /// <summary>Wraps a caller-managed client (its lifetime is not owned here).</summary>
    public HttpClientFetcher(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (!_client.DefaultRequestHeaders.UserAgent.Any())
        {
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        }

        _ownsClient = false;
    }

    public async Task<string> PostFormAsync(
        string url,
        IReadOnlyDictionary<string, string> form,
        CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _client.PostAsync(url, content, ct).ConfigureAwait(false);
        return await ReadOrThrowAsync(url, response, ct).ConfigureAwait(false);
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        using var response = await _client.GetAsync(url, ct).ConfigureAwait(false);
        return await ReadOrThrowAsync(url, response, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        using var response = await _client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpFetchException(url, (int)response.StatusCode, response.ReasonPhrase ?? "request failed");
        }

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadOrThrowAsync(string url, HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpFetchException(url, (int)response.StatusCode, response.ReasonPhrase ?? "request failed");
        }

        return body;
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
