using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;
using Xunit;

namespace RimManager.Core.Tests.Parsing;

public sealed class CommunityRulesParserTests
{
    private const string Json = """
        {
          "timestamp": 1,
          "rules": {
            "Author.B": {
              "loadAfter": { "author.a": { "comment": ["needs A first"] } },
              "loadBottom": { "value": true, "comment": "put me last" }
            },
            "author.c": {
              "loadBefore": { "author.a": { "comment": "before A" } },
              "loadTop": { "value": true }
            }
          }
        }
        """;

    [Fact]
    public void Parses_load_after_before_with_comments_and_normalizes_ids()
    {
        var rules = CommunityRulesParser.Parse(Json);

        var b = rules.Rules[ModId.From("author.b")];
        b.LoadAfter.Should().ContainSingle();
        b.LoadAfter[0].PackageId.Should().Be(ModId.From("author.a"));
        b.LoadAfter[0].Comment.Should().Be("needs A first");
        b.LoadBottom.Should().BeTrue();
        b.LoadBottomComment.Should().Be("put me last");

        var c = rules.Rules[ModId.From("author.c")];
        c.LoadBefore[0].Comment.Should().Be("before A");
        c.LoadTop.Should().BeTrue();
    }

    [Fact]
    public void Missing_rules_object_yields_empty()
    {
        CommunityRulesParser.Parse("""{"timestamp":1}""").Rules.Should().BeEmpty();
    }

    [Fact]
    public void Flag_without_value_true_is_false()
    {
        var rules = CommunityRulesParser.Parse("""
            { "rules": { "a.b": { "loadTop": { "comment": "x" } } } }
            """);
        rules.Rules[ModId.From("a.b")].LoadTop.Should().BeFalse();
    }
}
