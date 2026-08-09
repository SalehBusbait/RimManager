using FluentAssertions;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class SteamUrlsTests
{
    [Fact]
    public void Builds_the_client_deep_link_and_web_fallback()
    {
        SteamUrls.CommunityFilePage("2976993124")
            .Should().Be("steam://url/CommunityFilePage/2976993124");
        SteamUrls.WebFilePage("2976993124")
            .Should().Be("https://steamcommunity.com/sharedfiles/filedetails/?id=2976993124");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123abc")]
    [InlineData("123/CommunityFilePage/456")]
    [InlineData("123 456")]
    public void Rejects_non_numeric_ids_so_nothing_extra_can_be_smuggled_into_a_launch(string bad)
    {
        var act = () => SteamUrls.CommunityFilePage(bad);
        act.Should().Throw<ArgumentException>();
    }
}
