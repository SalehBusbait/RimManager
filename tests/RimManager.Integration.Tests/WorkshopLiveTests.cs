using System.Net.Http;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using RimManager.Integrations.Http;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Hits the real keyless Steam Workshop endpoint. Skips (never fails) when the
/// machine is offline or Steam is unreachable, so CI on an isolated runner stays
/// green — the same contract as the other live fixtures here. This is the one test
/// that exercises <see cref="HttpClientFetcher"/> end-to-end.
/// </summary>
public sealed class WorkshopLiveTests
{
    // HugsLib — a long-lived, widely-subscribed RimWorld mod; a stable id to probe.
    private const string HugsLibId = "818773962";

    [SkippableFact]
    public async Task Fetches_real_metadata_for_a_known_workshop_item()
    {
        using var fetcher = new HttpClientFetcher(TimeSpan.FromSeconds(15));
        var client = new SteamWorkshopClient(fetcher);

        var items = await FetchOrSkip(() => client.GetByIdAsync([HugsLibId]));

        items.Should().ContainKey(HugsLibId);
        var hugs = items[HugsLibId];
        hugs.Result.Should().Be(WorkshopItemResult.Ok);
        hugs.ConsumerAppId.Should().Be(SteamWorkshopClient.RimWorldAppId);
        hugs.Title.Should().NotBeNullOrWhiteSpace();
        hugs.TimeUpdatedUtc.Should().NotBeNull("the update time is what update-checking compares against");
    }

    [SkippableFact]
    public async Task Unknown_id_comes_back_as_not_found_over_the_wire()
    {
        using var fetcher = new HttpClientFetcher(TimeSpan.FromSeconds(15));
        var client = new SteamWorkshopClient(fetcher);

        // 1 is not a valid published-file id; Steam returns result 9 for it.
        var items = await FetchOrSkip(() => client.GetByIdAsync(["1"]));

        items.Should().ContainKey("1");
        items["1"].Result.Should().Be(WorkshopItemResult.NotFound);
    }

    private static async Task<T> FetchOrSkip<T>(Func<Task<T>> fetch)
    {
        try
        {
            return await fetch();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Skip.If(true, $"Steam Workshop API unreachable: {ex.Message}");
            throw; // unreachable; Skip.If throws
        }
    }
}
