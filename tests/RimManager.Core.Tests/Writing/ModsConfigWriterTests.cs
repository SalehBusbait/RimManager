using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;
using RimManager.Core.Writing;
using Xunit;

namespace RimManager.Core.Tests.Writing;

public sealed class ModsConfigWriterTests
{
    private static ModsConfig Sample() => new(
        "1.6.4871 rev590",
        [ModId.From("Brrainz.Harmony"), ModId.From("Ludeon.RimWorld")],
        [ModId.From("ludeon.rimworld.royalty")]);

    [Fact]
    public void Produces_the_exact_rimworld_byte_format()
    {
        const string expected =
            "<?xml version=\"1.0\" ?>\r\n" +
            "<ModsConfigData>\r\n" +
            "  <version>1.6.4871 rev590</version>\r\n" +
            "  <activeMods>\r\n" +
            "    <li>brrainz.harmony</li>\r\n" +
            "    <li>ludeon.rimworld</li>\r\n" +
            "  </activeMods>\r\n" +
            "  <knownExpansions>\r\n" +
            "    <li>ludeon.rimworld.royalty</li>\r\n" +
            "  </knownExpansions>\r\n" +
            "</ModsConfigData>\r\n";

        ModsConfigWriter.Serialize(Sample()).Should().Be(expected);
    }

    [Fact]
    public void Writes_lowercased_ids_but_from_any_input_casing()
    {
        // Input ids were mixed-case; output must be canonical lowercase (as RimWorld writes).
        ModsConfigWriter.Serialize(Sample()).Should().Contain("<li>brrainz.harmony</li>");
    }

    [Fact]
    public void Round_trips_through_the_parser()
    {
        var original = Sample();
        var reparsed = ModsConfigParser.Parse(ModsConfigWriter.Serialize(original));

        reparsed.Version.Should().Be(original.Version);
        reparsed.ActiveMods.Should().Equal(original.ActiveMods);
        reparsed.KnownExpansions.Should().Equal(original.KnownExpansions);
    }

    [Fact]
    public void Empty_known_expansions_is_self_closing()
    {
        var config = new ModsConfig("1.6", [ModId.From("a.b")], []);
        ModsConfigWriter.Serialize(config).Should().Contain("  <knownExpansions />\r\n");
    }

    [Fact]
    public void No_bom_in_bytes()
    {
        var bytes = ModsConfigWriter.SerializeToBytes(Sample());
        bytes[0].Should().Be((byte)'<', "there must be no UTF-8 BOM");
    }
}
