using FluentAssertions;
using RimManager.Core.Parsing;
using Xunit;

namespace RimManager.Core.Tests.Parsing;

public sealed class LoadFoldersParserTests
{
    // The real Harmony LoadFolders.xml shape.
    private const string Xml = """
        <loadFolders>
          <v1.4>
            <li>/</li>
            <li>1.4</li>
          </v1.4>
          <v1.6>
            <li>/</li>
            <li>Current</li>
          </v1.6>
        </loadFolders>
        """;

    [Fact]
    public void Resolves_folders_for_a_version_stripping_the_v_prefix()
    {
        var lf = LoadFolders.Parse(Xml);

        lf.HasVersion("1.6").Should().BeTrue();
        lf.FoldersFor("1.6").Should().Equal("/", "Current");
        lf.FoldersFor("1.4").Should().Equal("/", "1.4");
    }

    [Fact]
    public void Unknown_version_yields_no_folders()
    {
        LoadFolders.Parse(Xml).FoldersFor("1.5").Should().BeEmpty();
    }

    [Fact]
    public void AllFolders_unions_and_dedupes()
    {
        LoadFolders.Parse(Xml).AllFolders()
            .Should().BeEquivalentTo(["/", "1.4", "Current"]);
    }

    [Fact]
    public void Malformed_xml_is_empty_not_thrown()
    {
        var lf = LoadFolders.Parse("<loadFolders><v1.6><li>/</oops>");
        lf.Versions.Should().BeEmpty();
    }
}
