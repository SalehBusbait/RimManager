using FluentAssertions;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Confirms the committed <c>/fixtures</c> tree is present and shaped as the
/// scanner (Phase 1) will expect. These are real files captured from a live
/// 1,500-ish mod install. Skips (does not fail) if fixtures are absent, so a
/// fresh clone without them still goes green.
/// </summary>
public sealed class FixtureSmokeTests
{
    private static string? FixturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    [SkippableFact]
    public void ModsConfig_fixture_is_present_and_parseable()
    {
        var root = FixturesRoot();
        Skip.If(root is null, "No /fixtures directory found.");

        var modsConfig = Path.Combine(root!, "config", "ModsConfig.xml");
        Skip.IfNot(File.Exists(modsConfig), "ModsConfig.xml fixture not present.");

        var xml = System.Xml.Linq.XDocument.Load(modsConfig);
        xml.Root!.Name.LocalName.Should().Be("ModsConfigData");
        xml.Descendants("activeMods").Should().ContainSingle();
        xml.Descendants("activeMods").Single().Elements("li").Should().NotBeEmpty();
    }

    [SkippableFact]
    public void At_least_one_About_xml_fixture_is_present()
    {
        var root = FixturesRoot();
        Skip.If(root is null, "No /fixtures directory found.");

        var modsDir = Path.Combine(root!, "mods");
        Skip.IfNot(Directory.Exists(modsDir), "No mods fixtures present.");

        var abouts = Directory.GetFiles(modsDir, "About.xml", SearchOption.AllDirectories);
        abouts.Should().NotBeEmpty("Phase 1 scanner tests need real About.xml samples");
    }
}
