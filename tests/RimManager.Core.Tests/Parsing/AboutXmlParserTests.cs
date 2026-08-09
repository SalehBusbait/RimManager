using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;
using Xunit;

namespace RimManager.Core.Tests.Parsing;

public sealed class AboutXmlParserTests
{
    [Fact]
    public void Parses_core_fields()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ModMetaData>
              <name>Harmony</name>
              <author>Andreas Pardeike</author>
              <packageId>brrainz.harmony</packageId>
              <modVersion>2.4.2.0</modVersion>
              <supportedVersions>
                <li>1.5</li>
                <li>1.6</li>
              </supportedVersions>
              <loadBefore>
                <li>Ludeon.RimWorld</li>
              </loadBefore>
            </ModMetaData>
            """;

        var meta = AboutXmlParser.Parse(xml);

        meta.PackageId.Should().Be("brrainz.harmony");
        meta.Name.Should().Be("Harmony");
        meta.Authors.Should().Equal("Andreas Pardeike");
        meta.ModVersion.Should().Be("2.4.2.0");
        meta.SupportedVersions.Should().Equal("1.5", "1.6");
        meta.LoadBefore.Should().Equal("Ludeon.RimWorld");
        meta.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Splits_comma_separated_author_and_parses_dependencies()
    {
        const string xml = """
            <ModMetaData>
              <name>VFE Medical</name>
              <author>OskarPotocki, Atlas, Kikohi</author>
              <packageId>VanillaExpanded.VFEMedical</packageId>
              <modDependencies>
                <li>
                  <packageId>brrainz.harmony</packageId>
                  <displayName>Harmony</displayName>
                  <steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl>
                </li>
                <li>
                  <packageId>OskarPotocki.VanillaFactionsExpanded.Core</packageId>
                  <displayName>Vanilla Expanded Framework</displayName>
                </li>
              </modDependencies>
              <loadAfter>

                <li>OskarPotocki.VanillaFactionsExpanded.Core</li>
              </loadAfter>
            </ModMetaData>
            """;

        var meta = AboutXmlParser.Parse(xml);

        meta.Authors.Should().Equal("OskarPotocki", "Atlas", "Kikohi");
        meta.Dependencies.Should().HaveCount(2);
        meta.Dependencies[0].PackageId.Should().Be(ModId.From("brrainz.harmony"));
        meta.Dependencies[0].DisplayName.Should().Be("Harmony");
        meta.Dependencies[0].SteamWorkshopUrl.Should().Contain("2009463077");
        // The blank <li> gap must be skipped.
        meta.LoadAfter.Should().Equal("OskarPotocki.VanillaFactionsExpanded.Core");
    }

    [Fact]
    public void Prefers_authors_list_over_single_author()
    {
        const string xml = """
            <ModMetaData>
              <packageId>a.b</packageId>
              <name>N</name>
              <author>Ignored</author>
              <authors>
                <li>Alice</li>
                <li>Bob</li>
              </authors>
            </ModMetaData>
            """;

        AboutXmlParser.Parse(xml).Authors.Should().Equal("Alice", "Bob");
    }

    [Fact]
    public void Missing_packageId_is_an_error_warning_not_a_throw()
    {
        const string xml = "<ModMetaData><name>Nameless</name></ModMetaData>";

        var meta = AboutXmlParser.Parse(xml);

        meta.PackageId.Should().BeNull();
        meta.Warnings.Should().ContainSingle(w => w.Code == "about.missing-packageId"
            && w.Severity == WarningSeverity.Error);
    }

    [Fact]
    public void Invalid_xml_yields_an_error_warning_not_a_throw()
    {
        var meta = AboutXmlParser.Parse("<ModMetaData><name>oops</ModMetaData>");

        meta.Warnings.Should().ContainSingle(w => w.Code == "about.invalid-xml");
    }

    [Fact]
    public void Element_name_casing_is_tolerated()
    {
        const string xml = "<ModMetaData><PackageID>a.b</PackageID><Name>N</Name></ModMetaData>";

        var meta = AboutXmlParser.Parse(xml);

        meta.PackageId.Should().Be("a.b");
        meta.Name.Should().Be("N");
    }
}
