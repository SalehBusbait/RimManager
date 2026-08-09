using FluentAssertions;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class WorkshopManifestParserTests
{
    private const string Acf = """
        "AppWorkshop"
        {
            "appid"		"294100"
            "SizeOnDisk"		"1348172754"
            "TimeLastUpdated"		"1784960737"
            "WorkshopItemsInstalled"
            {
                "839005762"
                {
                    "size"		"12750138"
                    "timeupdated"		"1784165106"
                    "manifest"		"7562324342273202070"
                }
                "927155256"
                {
                    "size"		"3333168"
                    "timeupdated"		"1755068836"
                    "manifest"		"3392137142838353190"
                }
            }
        }
        """;

    [Fact]
    public void Parses_installed_items_with_timeupdated_size_and_manifest()
    {
        var state = WorkshopManifestParser.Parse(Acf);

        state.Items.Should().HaveCount(2);
        var item = state.TryGet("839005762")!;
        item.PublishedFileId.Should().Be("839005762");
        item.TimeUpdatedUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1784165106));
        item.SizeOnDisk.Should().Be(12750138);
        item.ManifestId.Should().Be("7562324342273202070");
    }

    [Fact]
    public void Empty_or_blank_input_yields_empty_state()
    {
        WorkshopManifestParser.Parse("").Items.Should().BeEmpty();
        WorkshopManifestParser.Parse("   ").Items.Should().BeEmpty();
    }

    [Fact]
    public void Missing_installed_section_yields_empty_state()
    {
        WorkshopManifestParser.Parse("""
            "AppWorkshop" { "appid" "294100" }
            """).Items.Should().BeEmpty();
    }

    [Fact]
    public void Absent_timeupdated_leaves_null_without_dropping_the_item()
    {
        var state = WorkshopManifestParser.Parse("""
            "AppWorkshop" { "WorkshopItemsInstalled" { "5" { "size" "10" } } }
            """);

        var item = state.TryGet("5")!;
        item.Should().NotBeNull();
        item.TimeUpdatedUtc.Should().BeNull();
        item.SizeOnDisk.Should().Be(10);
    }
}
