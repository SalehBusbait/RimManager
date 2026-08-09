using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class CollectionDetailsParserTests
{
    [Fact]
    public void Parses_members_in_sortorder_not_response_order()
    {
        const string json = """
            {
              "response": {
                "result": 1,
                "resultcount": 1,
                "collectiondetails": [
                  {
                    "publishedfileid": "555",
                    "result": 1,
                    "children": [
                      { "publishedfileid": "300", "sortorder": 2, "filetype": 0 },
                      { "publishedfileid": "100", "sortorder": 0, "filetype": 0 },
                      { "publishedfileid": "200", "sortorder": 1, "filetype": 0 }
                    ]
                  }
                ]
              }
            }
            """;

        var collection = CollectionDetailsParser.Parse(json).Single();
        collection.CollectionId.Should().Be("555");
        collection.Result.Should().Be(WorkshopItemResult.Ok);
        collection.MemberIds.Should().Equal("100", "200", "300");
    }

    [Fact]
    public void Not_found_collection_is_surfaced_with_no_members()
    {
        const string json = """
            { "response": { "collectiondetails": [ { "publishedfileid": "1", "result": 9 } ] } }
            """;

        var collection = CollectionDetailsParser.Parse(json).Single();
        collection.Result.Should().Be(WorkshopItemResult.NotFound);
        collection.IsOk.Should().BeFalse();
        collection.MemberIds.Should().BeEmpty();
    }

    [Fact]
    public void Missing_children_yields_empty_member_list()
    {
        const string json = """
            { "response": { "collectiondetails": [ { "publishedfileid": "7", "result": 1 } ] } }
            """;

        CollectionDetailsParser.Parse(json).Single().MemberIds.Should().BeEmpty();
    }

    [Fact]
    public void Missing_envelope_yields_empty()
    {
        CollectionDetailsParser.Parse("""{"response":{}}""").Should().BeEmpty();
    }
}
