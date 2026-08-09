using FluentAssertions;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class WorkshopUrlTests
{
    [Theory]
    [InlineData("12345", "12345")]
    [InlineData("  12345  ", "12345")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=12345", "12345")]
    [InlineData("https://steamcommunity.com/workshop/filedetails/?id=12345&searchtext=x", "12345")]
    [InlineData("http://steamcommunity.com/sharedfiles/filedetails/?searchtext=x&id=999", "999")]
    [InlineData("steam://url/CommunityFilePage/778", "778")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=12345#comments", "12345")]
    public void Extracts_id_from_supported_forms(string input, string expected)
    {
        WorkshopUrl.TryGetId(input, out var id).Should().BeTrue();
        id.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=abc")]
    [InlineData("https://example.com/page")]
    public void Rejects_input_without_a_numeric_id(string? input)
    {
        WorkshopUrl.TryGetId(input, out var id).Should().BeFalse();
        id.Should().BeNull();
    }
}
