using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// <see cref="KnownMods.DisplayName"/> exists because of a fact measured on the real
/// install, not a guess: <b>every one of Ludeon's own About.xml files omits
/// &lt;name&gt;</b>. Core, Royalty, Ideology, Biotech, Anomaly and Odyssey each carry a
/// packageId, an author and a version list, and nothing else — so the scanner's
/// packageId fallback made the six rows that anchor every load order read
/// "Ludeon.RimWorld.Anomaly".
/// </summary>
public sealed class KnownModsTests
{
    [Fact]
    public void The_base_game_is_named_RimWorld_rather_than_its_packageId()
    {
        KnownMods.DisplayName(ModId.From("Ludeon.RimWorld")).Should().Be("RimWorld");
    }

    /// <summary>
    /// The five expansions on the real install, in the exact casing Ludeon ships.
    /// </summary>
    [Theory]
    [InlineData("Ludeon.RimWorld.Royalty", "Royalty")]
    [InlineData("Ludeon.RimWorld.Ideology", "Ideology")]
    [InlineData("Ludeon.RimWorld.Biotech", "Biotech")]
    [InlineData("Ludeon.RimWorld.Anomaly", "Anomaly")]
    [InlineData("Ludeon.RimWorld.Odyssey", "Odyssey")]
    public void An_expansion_is_named_by_the_last_segment_of_its_packageId(
        string packageId, string expected)
    {
        KnownMods.DisplayName(ModId.From(packageId)).Should().Be(expected);
    }

    /// <summary>
    /// The reason the DLC name is derived rather than tabled: Odyssey shipped after
    /// Anomaly, and the next one will ship after this test. Deriving it means a new
    /// expansion is named correctly with no change here.
    /// </summary>
    [Fact]
    public void An_expansion_that_does_not_exist_yet_is_still_named()
    {
        KnownMods.DisplayName(ModId.From("Ludeon.RimWorld.SomethingLater"))
            .Should().Be("SomethingLater");
    }

    /// <summary>
    /// Identity is case-insensitive, but the NAME keeps the casing as authored — the
    /// same split <see cref="ModId"/> itself makes.
    /// </summary>
    [Fact]
    public void The_name_keeps_the_casing_the_packageId_was_authored_in()
    {
        KnownMods.DisplayName(ModId.From("ludeon.rimworld.royalty")).Should().Be("royalty");
    }

    /// <summary>
    /// Nothing but Ludeon's own ids resolves, so a mod that merely looks official is
    /// left to its About.xml. Naming someone else's mod after its packageId segment
    /// would be worse than the packageId, because it would look deliberate.
    /// </summary>
    [Theory]
    [InlineData("brrainz.harmony")]
    [InlineData("Jaxe.RimHUD")]
    [InlineData("ludeonstudios.rimworld.fake")]
    [InlineData("notludeon.rimworld.royalty")]
    public void Anything_that_is_not_Ludeons_has_no_display_name(string packageId)
    {
        KnownMods.DisplayName(ModId.From(packageId)).Should().BeNull();
    }
}
