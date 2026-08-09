using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;
using Xunit;

namespace RimManager.Core.Tests.Parsing;

public sealed class ModsConfigParserTests
{
    private const string Xml = """
        <?xml version="1.0" ?>
        <ModsConfigData>
          <version>1.6.4871 rev590</version>
          <activeMods>
            <li>brrainz.harmony</li>
            <li>Ludeon.RimWorld</li>
            <li>vanillaexpanded.vfecore</li>
          </activeMods>
          <knownExpansions>
            <li>ludeon.rimworld.royalty</li>
            <li>ludeon.rimworld.biotech</li>
          </knownExpansions>
        </ModsConfigData>
        """;

    [Fact]
    public void Parses_version_and_preserves_active_order()
    {
        var config = ModsConfigParser.Parse(Xml);

        config.Version.Should().Be("1.6.4871 rev590");
        config.ActiveMods.Select(m => m.Value)
            .Should().Equal("brrainz.harmony", "ludeon.rimworld", "vanillaexpanded.vfecore");
    }

    [Fact]
    public void Normalizes_casing_to_ids()
    {
        var config = ModsConfigParser.Parse(Xml);

        // "Ludeon.RimWorld" in the file must equal the lowercased id used everywhere.
        config.ActiveMods.Should().Contain(ModId.From("ludeon.rimworld"));
    }

    [Fact]
    public void Extracts_major_minor()
    {
        ModsConfigParser.Parse(Xml).MajorMinor.Should().Be("1.6");
    }

    [Fact]
    public void Reads_known_expansions()
    {
        ModsConfigParser.Parse(Xml).KnownExpansions
            .Should().Equal(ModId.From("ludeon.rimworld.royalty"), ModId.From("ludeon.rimworld.biotech"));
    }
}
