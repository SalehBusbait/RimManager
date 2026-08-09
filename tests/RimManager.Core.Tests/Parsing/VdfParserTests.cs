using FluentAssertions;
using RimManager.Core.Parsing;
using Xunit;

namespace RimManager.Core.Tests.Parsing;

public sealed class VdfParserTests
{
    // Shape mirrors a real libraryfolders.vdf, including escaped backslashes.
    private const string Vdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"apps"
        		{
        			"228980"		"318229767"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"apps"
        		{
        			"294100"		"964789840"
        		}
        	}
        }
        """;

    [Fact]
    public void Parses_nested_libraries_and_unescapes_paths()
    {
        var root = VdfParser.Parse(Vdf);
        var libraries = root["libraryfolders"];

        libraries.Should().NotBeNull();
        libraries!["1"]!["path"]!.Value.Should().Be(@"D:\SteamLibrary");
    }

    [Fact]
    public void Exposes_apps_membership()
    {
        var lib1 = VdfParser.Parse(Vdf)["libraryfolders"]!["1"]!;

        lib1["apps"]!.Children.ContainsKey("294100").Should().BeTrue();
        lib1["apps"]!.Children.ContainsKey("999999").Should().BeFalse();
    }

    [Fact]
    public void Key_lookup_is_case_insensitive()
    {
        VdfParser.Parse(Vdf)["LIBRARYFOLDERS"].Should().NotBeNull();
    }

    [Fact]
    public void Skips_line_comments()
    {
        const string withComment = """
            "root"
            {
                // a comment
                "k"  "v"
            }
            """;

        VdfParser.Parse(withComment)["root"]!["k"]!.Value.Should().Be("v");
    }
}
