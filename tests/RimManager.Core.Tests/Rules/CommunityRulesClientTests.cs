using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Rules;

public sealed class CommunityRulesClientTests
{
    private const string Db = """
        {
          "timestamp": 1777950016,
          "rules": {
            "Author.B": { "loadAfter": { "author.a": { "comment": ["needs A"] } } },
            "author.c": { "loadBottom": { "value": true } }
          }
        }
        """;

    [Fact]
    public async Task Fetches_from_default_url_and_parses_rules_and_timestamp()
    {
        string? requested = null;
        var fetcher = new FakeHttpFetcher { GetResponder = url => { requested = url; return Db; } };
        var client = new CommunityRulesClient(fetcher);

        var db = await client.FetchAsync();

        requested.Should().Be(CommunityRulesClient.DefaultUrl);
        db.RuleCount.Should().Be(2);
        db.Rules.Rules[ModId.From("author.b")].LoadAfter.Should().ContainSingle();
        db.PublishedUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1777950016));
        db.RawJson.Should().Be(Db);
    }

    [Fact]
    public async Task Honors_an_override_url()
    {
        string? requested = null;
        var fetcher = new FakeHttpFetcher { GetResponder = url => { requested = url; return Db; } };
        var client = new CommunityRulesClient(fetcher);

        await client.FetchAsync("https://example/mirror.json");

        requested.Should().Be("https://example/mirror.json");
    }

    [Fact]
    public void Build_tolerates_a_missing_timestamp()
    {
        var db = CommunityRulesClient.Build("""{ "rules": { "a.b": {} } }""");
        db.PublishedUtc.Should().BeNull();
        db.RuleCount.Should().Be(1);
    }
}
