using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Workshop descriptions arrive full of BBCode. The info pane shows a clamped
/// four-line summary (1a §8), and markup makes those four lines worthless.
/// </summary>
public sealed class BbCodeTests
{
    [Fact]
    public void Strips_simple_formatting_tags() =>
        BbCode.Strip("[b]Bold[/b] and [i]italic[/i]").Should().Be("Bold and italic");

    [Fact]
    public void Strips_url_tags_with_attributes() =>
        BbCode.Strip("See [url=https://github.com/x/y]the repo[/url] for more")
            .Should().Be("See the repo for more");

    /// <summary>A list item loses its marker with its tag, so one is put back —
    /// the bullets are usually the only structure worth keeping.</summary>
    [Fact]
    public void Keeps_list_items_readable_as_bullets() =>
        BbCode.Strip("[list][*]First[*]Second[/list]").Should().Contain("· First").And.Contain("· Second");

    [Fact]
    public void Handles_the_nested_soup_real_descriptions_contain()
    {
        var input = "[b][u][url=https://steamcommunity.com/workshop/filedetails/?id=1909914131]"
                  + "Save Our Ship 2[/url][/u][/b]";

        BbCode.Strip(input).Should().Be("Save Our Ship 2");
    }

    [Fact]
    public void Collapses_runs_of_blank_lines_left_behind_by_removed_tags()
    {
        var stripped = BbCode.Strip("One[/list]\n\n\n\n\n[list]Two")!;

        stripped.Should().NotContain("\n\n\n");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[b][/b]")]
    public void Returns_null_when_nothing_readable_survives(string? input) =>
        BbCode.Strip(input).Should().BeNull();

    [Fact]
    public void Leaves_plain_prose_untouched() =>
        BbCode.Strip("Overhauls combat: ballistics, ammunition, loadouts.")
            .Should().Be("Overhauls combat: ballistics, ammunition, loadouts.");

    /// <summary>Square brackets that are not tags must survive — mod names use them.</summary>
    [Fact]
    public void Leaves_non_tag_brackets_alone() =>
        BbCode.Strip("[sbz] Neat Storage").Should().Be("[sbz] Neat Storage");
}
