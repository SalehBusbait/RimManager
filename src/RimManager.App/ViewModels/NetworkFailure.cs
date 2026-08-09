using System.Net.Http;
using System.Net.Sockets;
using RimManager.Core.Abstractions;

namespace RimManager.App.ViewModels;

/// <summary>
/// Tells "we could not reach the network" apart from "the server answered and said
/// no", which is the distinction <c>2k</c>'s offline state rests on.
/// <para>
/// RimManager never claims to know the state of the network — it has not asked the
/// operating system and would not believe the answer if it had (a VPN, a captive
/// portal and a proxy all report a live connection). It reports what happened to the
/// last request it actually made. That is why the strip says what is paused rather
/// than "you are offline".
/// </para>
/// </summary>
public static class NetworkFailure
{
    /// <summary>
    /// True when the request never got an answer.
    /// <para>
    /// <see cref="HttpFetchException"/> is deliberately NOT connectivity: it means a
    /// server replied with a status we did not want. A 404 from Steam for a delisted
    /// mod would otherwise put the whole app into an offline state.
    /// </para>
    /// </summary>
    public static bool IsConnectivity(Exception? ex) => ex switch
    {
        null => false,
        HttpFetchException => false,
        HttpRequestException => true,
        SocketException => true,

        // An HttpClient timeout surfaces as TaskCanceledException with no cancellation
        // asked for. A genuine user cancellation carries a token that IS cancelled.
        TaskCanceledException canceled => !canceled.CancellationToken.IsCancellationRequested,
        TimeoutException => true,

        // Wrappers: a client that catches and rethrows still has the real cause inside.
        _ => ex.InnerException is not null && IsConnectivity(ex.InnerException),
    };

    /// <summary>
    /// The strip's second line. Names what is still working first, because the point of
    /// per-feature degradation is that almost everything is.
    /// </summary>
    /// <param name="rulesCount">Community rules currently loaded from the cache.</param>
    /// <param name="rulesAge">How long ago they were synced, or null if never.</param>
    public static string Detail(int rulesCount, TimeSpan? rulesAge) => rulesCount <= 0
        ? "Everything else works. There are no cached rules, so sorting uses About.xml only."
        : $"Everything else works. Cached rules from {Age(rulesAge)} are in use.";

    /// <summary>
    /// "2 days ago", "4 hours ago", "just now". Coarse on purpose: the question this
    /// answers is "are my rules roughly current", not "how many minutes".
    /// </summary>
    public static string Age(TimeSpan? age) => age switch
    {
        null => "an unknown date",
        { TotalMinutes: < 2 } => "just now",
        { TotalHours: < 1 } t => $"{(int)t.TotalMinutes} minutes ago",
        { TotalDays: < 1 } t => $"{(int)t.TotalHours} hour{S((int)t.TotalHours)} ago",
        { TotalDays: < 30 } t => $"{(int)t.TotalDays} day{S((int)t.TotalDays)} ago",
        var t => $"{(int)(t.Value.TotalDays / 30)} month{S((int)(t.Value.TotalDays / 30))} ago",
    };

    /// <summary>The status bar's zone 2, which is 24px high and has no room for a sentence.</summary>
    public static string RulesStatus(int rulesCount, TimeSpan? rulesAge, bool offline)
    {
        if (rulesCount <= 0) return "No community rules";

        var when = offline ? "cached" : rulesAge is null ? "synced" : $"synced {Short(rulesAge)}";
        return $"Community rules {rulesCount:N0} · {when}";
    }

    /// <summary>"2d", "4h", "now" — zone 2 is 24px high.</summary>
    private static string Short(TimeSpan? age) => age switch
    {
        null => "—",
        { TotalHours: < 1 } => "now",
        { TotalDays: < 1 } t => $"{(int)t.TotalHours}h ago",
        { TotalDays: < 30 } t => $"{(int)t.TotalDays}d ago",
        var t => $"{(int)(t.Value.TotalDays / 30)}mo ago",
    };

    private static string S(int n) => n == 1 ? "" : "s";
}
