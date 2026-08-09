using FluentAssertions;
using RimManager.Core.Locators;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Locators;

public sealed class SteamLibraryLocatorTests
{
    private static InMemoryFileSystem Fs() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-07-24T00:00:00Z")));

    private static string Vdf(string libPath) => $$"""
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"{{libPath}}"
        		"apps"
        		{
        			"294100"		"964789840"
        		}
        	}
        }
        """;

    [Fact]
    public void Finds_rimworld_install_and_workshop_from_vdf()
    {
        var fs = Fs();
        fs.AddFile("/steam/steamapps/libraryfolders.vdf", Vdf("/lib"));
        fs.AddFile("/lib/steamapps/common/RimWorld/Version.txt", "1.6");
        fs.AddFile("/lib/steamapps/workshop/content/294100/123/About/About.xml", "<ModMetaData/>");

        var env = new FakePlatformEnvironment { SteamClientRoots = ["/steam"] };
        var installs = SteamLibraryLocator.Locate(env, fs);

        installs.Should().ContainSingle();
        installs[0].GameDir.Replace('\\', '/').Should().Be("/lib/steamapps/common/RimWorld");
        installs[0].WorkshopDir!.Replace('\\', '/').Should().Be("/lib/steamapps/workshop/content/294100");
        installs[0].Kind.Should().Be(InstallKind.Steam);
    }

    [Fact]
    public void Ignores_libraries_that_lack_the_rimworld_app()
    {
        var fs = Fs();
        fs.AddFile("/steam/steamapps/libraryfolders.vdf", """
            "libraryfolders" { "0" { "path" "/lib" "apps" { "730" "1" } } }
            """);
        fs.AddFile("/lib/steamapps/common/RimWorld/Version.txt", "1.6");

        var env = new FakePlatformEnvironment { SteamClientRoots = ["/steam"] };
        SteamLibraryLocator.Locate(env, fs).Should().BeEmpty();
    }

    [Fact]
    public void Workshop_dir_is_null_when_absent()
    {
        var fs = Fs();
        fs.AddFile("/steam/steamapps/libraryfolders.vdf", Vdf("/lib"));
        fs.AddFile("/lib/steamapps/common/RimWorld/Version.txt", "1.6");
        // no workshop dir

        var env = new FakePlatformEnvironment { SteamClientRoots = ["/steam"] };
        SteamLibraryLocator.Locate(env, fs)[0].WorkshopDir.Should().BeNull();
    }
}
