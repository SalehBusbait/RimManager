using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class WorkshopMetadataParserTests
{
    // A realistic GetPublishedFileDetails response: one resolved item (HugsLib) with
    // Steam's quirks — file_size as a string, times as unix-second numbers, tags as
    // {tag} objects, children as {publishedfileid} objects — plus one not-found id.
    private const string Response = """
        {
          "response": {
            "result": 1,
            "resultcount": 2,
            "publishedfiledetails": [
              {
                "publishedfileid": "818773962",
                "result": 1,
                "creator": "76561198094109289",
                "consumer_app_id": 294100,
                "file_size": "1258291",
                "title": "HugsLib",
                "description": "A lightweight modding library.",
                "time_created": 1478901234,
                "time_updated": 1600000000,
                "visibility": 0,
                "banned": 0,
                "tags": [ { "tag": "Mod" }, { "tag": "1.5" } ],
                "children": [ { "publishedfileid": "2009463077", "sortorder": 0, "file_type": 0 } ]
              },
              {
                "publishedfileid": "1",
                "result": 9
              }
            ]
          }
        }
        """;

    [Fact]
    public void Parses_resolved_item_and_normalizes_steam_quirks()
    {
        var items = WorkshopMetadataParser.Parse(Response);

        var hugs = items.Single(i => i.PublishedFileId == "818773962");
        hugs.Result.Should().Be(WorkshopItemResult.Ok);
        hugs.IsOk.Should().BeTrue();
        hugs.Title.Should().Be("HugsLib");
        hugs.ConsumerAppId.Should().Be(SteamWorkshopClient.RimWorldAppId);
        hugs.Creator.Should().Be("76561198094109289");
        hugs.FileSize.Should().Be(1258291);                     // string → long
        hugs.TimeCreatedUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1478901234));
        hugs.TimeUpdatedUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1600000000));
        hugs.Tags.Should().Equal("Mod", "1.5");
        hugs.Children.Should().Equal("2009463077");
    }

    [Fact]
    public void Not_found_id_is_surfaced_not_dropped()
    {
        var items = WorkshopMetadataParser.Parse(Response);

        var missing = items.Single(i => i.PublishedFileId == "1");
        missing.Result.Should().Be(WorkshopItemResult.NotFound);
        missing.IsOk.Should().BeFalse();
        missing.Title.Should().BeNull();
    }

    [Fact]
    public void Missing_response_envelope_yields_empty()
    {
        WorkshopMetadataParser.Parse("""{"foo":1}""").Should().BeEmpty();
        WorkshopMetadataParser.Parse("""{"response":{}}""").Should().BeEmpty();
    }

    [Fact]
    public void Zero_or_missing_times_map_to_null()
    {
        var items = WorkshopMetadataParser.Parse("""
            { "response": { "publishedfiledetails": [
                { "publishedfileid": "5", "result": 1, "time_updated": 0 } ] } }
            """);

        var item = items.Single();
        item.TimeUpdatedUtc.Should().BeNull();
        item.TimeCreatedUtc.Should().BeNull();
        item.FileSize.Should().BeNull();
        item.Tags.Should().BeEmpty();
        item.Children.Should().BeEmpty();
    }
}
