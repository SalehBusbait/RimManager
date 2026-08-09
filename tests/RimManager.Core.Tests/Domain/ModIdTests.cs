using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

public sealed class ModIdTests
{
    [Fact]
    public void Casing_differences_are_the_same_identity()
    {
        // The real-world case: About.xml ships "Jaxe.RimHUD", ModsConfig lists "jaxe.rimhud".
        var fromAbout = ModId.From("Jaxe.RimHUD");
        var fromConfig = ModId.From("jaxe.rimhud");

        fromAbout.Should().Be(fromConfig);
        fromAbout.GetHashCode().Should().Be(fromConfig.GetHashCode());
    }

    [Fact]
    public void Value_is_lowercased_but_display_preserves_original()
    {
        var id = ModId.From("Oskarpotocki.VanillaFactionsExpanded.Core");

        id.Value.Should().Be("oskarpotocki.vanillafactionsexpanded.core");
        id.Display.Should().Be("Oskarpotocki.VanillaFactionsExpanded.Core");
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        ModId.From("  brrainz.harmony  ").Value.Should().Be("brrainz.harmony");
    }

    [Fact]
    public void Works_as_a_dictionary_key_across_casing()
    {
        var map = new Dictionary<ModId, int> { [ModId.From("Brrainz.Harmony")] = 1 };

        map.ContainsKey(ModId.From("brrainz.harmony")).Should().BeTrue();
        map[ModId.From("BRRAINZ.HARMONY")].Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryFrom_rejects_empty(string? input)
    {
        ModId.TryFrom(input, out _).Should().BeFalse();
    }
}
