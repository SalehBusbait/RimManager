using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;
using RimManager.Core.Scanning;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>Scans the real committed About.xml fixtures via the physical filesystem.</summary>
public sealed class ScanFixturesTests
{
    [SkippableFact]
    public void Scans_real_about_fixtures_and_parses_harmony()
    {
        var modsDir = Fixtures.ModsDir();
        Skip.If(modsDir is null, "No /fixtures/mods present.");

        var fs = new PhysicalFileSystem();
        var result = new ModScanner(fs).Scan([new ModSourceRoot(modsDir!, ModSource.Workshop)], "1.6");

        var harmony = result.ById.GetValueOrDefault(ModId.From("brrainz.harmony"));
        harmony.Should().NotBeNull();
        harmony!.Name.Should().Be("Harmony");
        harmony.SupportedVersions.Should().Contain("1.6");
        harmony.ModVersion.Should().Be("2.4.2.0");
    }

    [SkippableFact]
    public void Parses_dependencies_and_load_rules_from_fixtures()
    {
        var modsDir = Fixtures.ModsDir();
        Skip.If(modsDir is null, "No /fixtures/mods present.");

        var fs = new PhysicalFileSystem();
        var result = new ModScanner(fs).Scan([new ModSourceRoot(modsDir!, ModSource.Workshop)], "1.6");

        var vfeMedical = result.ById.GetValueOrDefault(ModId.From("vanillaexpanded.vfemedical"));
        vfeMedical.Should().NotBeNull();
        vfeMedical!.Dependencies.Select(d => d.PackageId)
            .Should().Contain(ModId.From("brrainz.harmony"));
        vfeMedical.LoadAfter.Should().Contain(ModId.From("oskarpotocki.vanillafactionsexpanded.core"));
    }

    [SkippableFact]
    public void Reads_real_mods_config_fixture()
    {
        var path = Fixtures.ModsConfig();
        Skip.If(path is null, "No ModsConfig.xml fixture present.");

        var config = ModsConfigParser.Parse(File.ReadAllText(path!));

        config.MajorMinor.Should().Be("1.6");
        config.Version.Should().StartWith("1.6");
        config.ActiveMods.First().Should().Be(ModId.From("brrainz.harmony"));
        config.ActiveMods.Length.Should().BeGreaterThan(100);
    }
}
