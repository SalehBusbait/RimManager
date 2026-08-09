using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.ModDatabases;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.ModDatabases;

/// <summary>The NoVersionWarning list (N7): known-good packageIds per game version.</summary>
public sealed class NoVersionWarningTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ModIdsToFix>
          <!-- Another Milk Retexture -->
          <li>Mallow.MilkRetexture</li>
          <li>  Grizzlemethis.TradingSpot.RW  </li>
          <li></li>
        </ModIdsToFix>
        """;

    [Fact]
    public void Parses_ids_trims_whitespace_and_ignores_comments_and_empties()
    {
        var db = NoVersionWarningParser.Parse(SampleXml);

        db.Count.Should().Be(2);
        db.Contains(ModId.From("mallow.milkretexture")).Should().BeTrue("packageId identity is case-insensitive");
        db.Contains(ModId.From("grizzlemethis.tradingspot.rw")).Should().BeTrue();
        db.Contains(ModId.From("not.listed")).Should().BeFalse();
    }

    [Fact]
    public void Garbage_and_wrong_roots_yield_the_empty_database_never_a_throw()
    {
        NoVersionWarningParser.Parse("not xml").Count.Should().Be(0);
        NoVersionWarningParser.Parse("<SomethingElse><li>a.b</li></SomethingElse>").Count.Should().Be(0);
        NoVersionWarningParser.Parse("").Count.Should().Be(0);
    }

    [Fact]
    public async Task The_client_builds_the_per_version_url()
    {
        var fetcher = new FakeHttpFetcher { GetResponder = _ => SampleXml };
        var db = await new NoVersionWarningClient(fetcher).FetchAsync("1.6");

        db.Count.Should().Be(2);
        fetcher.GetCalls.Should().ContainSingle()
            .Which.Should().Be("https://raw.githubusercontent.com/emipa606/NoVersionWarning/main/1.6/ModIdsToFix.xml");
    }

    [Fact]
    public async Task A_version_with_no_list_yet_is_an_absence_not_an_error()
    {
        // The day a new RimWorld version ships, the upstream file does not exist.
        var fetcher = new FakeHttpFetcher { GetResponder = _ => null };

        var db = await new NoVersionWarningClient(fetcher).FetchAsync("1.7");
        db.Count.Should().Be(0);
    }
}
