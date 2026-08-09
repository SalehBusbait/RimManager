using System.Text;
using FluentAssertions;
using RimManager.Core.Tests.Fakes;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class SteamWorkshopClientTests
{
    /// <summary>Synthesizes a response that echoes each requested id back as an OK item.</summary>
    private static string EchoResponse(IReadOnlyDictionary<string, string> form)
    {
        var sb = new StringBuilder("""{"response":{"result":1,"publishedfiledetails":[""");
        var ids = form.Where(kv => kv.Key.StartsWith("publishedfileids[", StringComparison.Ordinal))
            .Select(kv => kv.Value).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($$"""{"publishedfileid":"{{ids[i]}}","result":1,"title":"item {{ids[i]}}"}""");
        }

        return sb.Append("]}}").ToString();
    }

    [Fact]
    public async Task Builds_steam_indexed_form_with_itemcount()
    {
        var fetcher = new FakeHttpFetcher { PostResponder = (_, form) => EchoResponse(form) };
        var client = new SteamWorkshopClient(fetcher);

        await client.GetPublishedFileDetailsAsync(["111", "222"]);

        fetcher.PostCalls.Should().ContainSingle();
        var (url, sentForm) = fetcher.PostCalls[0];
        url.Should().Be(SteamWorkshopClient.PublishedFileDetailsUrl);
        sentForm["itemcount"].Should().Be("2");
        sentForm["publishedfileids[0]"].Should().Be("111");
        sentForm["publishedfileids[1]"].Should().Be("222");
    }

    [Fact]
    public async Task Dedupes_and_drops_blank_ids_before_requesting()
    {
        var fetcher = new FakeHttpFetcher { PostResponder = (_, form) => EchoResponse(form) };
        var client = new SteamWorkshopClient(fetcher);

        var items = await client.GetPublishedFileDetailsAsync(["111", " 111 ", "", "  ", "222"]);

        var sentForm = fetcher.PostCalls.Single().Form;
        sentForm["itemcount"].Should().Be("2");
        items.Select(i => i.PublishedFileId).Should().BeEquivalentTo("111", "222");
    }

    [Fact]
    public async Task Empty_input_makes_no_request()
    {
        var fetcher = new FakeHttpFetcher();
        var client = new SteamWorkshopClient(fetcher);

        var items = await client.GetPublishedFileDetailsAsync(["", "   "]);

        items.Should().BeEmpty();
        fetcher.PostCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Batches_large_id_sets_and_stitches_results()
    {
        var fetcher = new FakeHttpFetcher { PostResponder = (_, form) => EchoResponse(form) };
        var client = new SteamWorkshopClient(fetcher);

        var ids = Enumerable.Range(1, 250).Select(i => i.ToString()).ToArray();
        var items = await client.GetPublishedFileDetailsAsync(ids);

        fetcher.PostCalls.Should().HaveCount(3);                 // 100 + 100 + 50
        fetcher.PostCalls[0].Form["itemcount"].Should().Be("100");
        fetcher.PostCalls[2].Form["itemcount"].Should().Be("50");
        items.Should().HaveCount(250);
        items.Select(i => i.PublishedFileId).Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task GetByIdAsync_keys_results_by_id()
    {
        var fetcher = new FakeHttpFetcher { PostResponder = (_, form) => EchoResponse(form) };
        var client = new SteamWorkshopClient(fetcher);

        var map = await client.GetByIdAsync(["111", "222"]);

        map.Should().ContainKeys("111", "222");
        map["111"].Title.Should().Be("item 111");
    }

    [Fact]
    public async Task GetCollectionAsync_posts_single_collection_form_and_returns_members()
    {
        const string body = """
            { "response": { "result": 1, "collectiondetails": [
                { "publishedfileid": "555", "result": 1, "children": [
                    { "publishedfileid": "100", "sortorder": 0 },
                    { "publishedfileid": "200", "sortorder": 1 } ] } ] } }
            """;
        var fetcher = new FakeHttpFetcher { PostResponder = (_, _) => body };
        var client = new SteamWorkshopClient(fetcher);

        var collection = await client.GetCollectionAsync("555");

        var (url, form) = fetcher.PostCalls.Single();
        url.Should().Be(SteamWorkshopClient.CollectionDetailsUrl);
        form["collectioncount"].Should().Be("1");
        form["publishedfileids[0]"].Should().Be("555");
        collection!.MemberIds.Should().Equal("100", "200");
    }
}
