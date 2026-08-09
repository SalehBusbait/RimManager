using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// Design non-negotiable #6: tag and separator colours persist as a palette index,
/// never a hex string, so a user's colours flip correctly with the theme.
/// </summary>
public sealed class PaletteTests
{
    [Fact]
    public void Palette_is_exactly_six_hues() => Palette.Count.Should().Be(6);

    [Fact]
    public void Named_hues_match_their_index()
    {
        Palette.NameOf(Palette.Blue).Should().Be("Blue");
        Palette.NameOf(Palette.Green).Should().Be("Green");
        Palette.NameOf(Palette.Amber).Should().Be("Amber");
        Palette.NameOf(Palette.Red).Should().Be("Red");
        Palette.NameOf(Palette.Violet).Should().Be("Violet");
        Palette.NameOf(Palette.Slate).Should().Be("Slate");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(-1, 5)]
    [InlineData(-6, 0)]
    public void Normalize_wraps_in_both_directions(int input, int expected) =>
        Palette.Normalize(input).Should().Be(expected);

    [Fact]
    public void Next_cycles_and_wraps_at_the_end()
    {
        Palette.Next(0).Should().Be(1);
        Palette.Next(4).Should().Be(5);
        Palette.Next(5).Should().Be(0);
    }

    // --- migration ----------------------------------------------------------

    /// <summary>Each hue's own reference value must map back to itself, or the
    /// migration would shuffle every existing colour.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NearestTo_is_the_identity_on_its_own_reference_hex(int index) =>
        Palette.NearestTo(Palette.ReferenceHex(index)).Should().Be(index);

    /// <summary>
    /// The colours the pre-migration builds actually wrote. These are the values on
    /// real users' disks, so their landing spots are the thing worth pinning.
    /// </summary>
    [Theory]
    [InlineData("#5B9BD5", Palette.Blue)]
    [InlineData("#4CAF50", Palette.Green)]
    [InlineData("#E0A030", Palette.Amber)]
    [InlineData("#D9534F", Palette.Red)]
    [InlineData("#9C6ADE", Palette.Violet)]
    // The legacy DEFAULT separator colour. It is a periwinkle sitting between the
    // two hues; both Euclidean RGB and HSV hue put it nearer blue than violet, so
    // most users' first separator comes back blue. Pinned because it is the single
    // most common value on disk.
    [InlineData("#7F77DD", Palette.Blue)]
    public void NearestTo_maps_the_legacy_separator_palette_sensibly(string hex, int expected) =>
        Palette.NearestTo(hex).Should().Be(expected);

    [Theory]
    [InlineData("5B9DF9")]   // no leading hash
    [InlineData("#5b9df9")]  // lower case
    [InlineData("#FF5B9DF9")] // #AARRGGBB
    public void NearestTo_accepts_the_hex_shapes_that_occur_in_the_wild(string hex) =>
        Palette.NearestTo(hex).Should().Be(Palette.Blue);

    [Fact]
    public void NearestTo_expands_three_digit_shorthand() =>
        Palette.NearestTo("#F00").Should().Be(Palette.Red);

    /// <summary>
    /// Tolerance is deliberate: losing a user's whole tag list because one colour was
    /// hand-edited to "puce" would be a far worse outcome than one tag coming back
    /// the wrong shade.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("puce")]
    [InlineData("#12")]
    public void NearestTo_falls_back_rather_than_throwing(string? hex) =>
        Palette.NearestTo(hex).Should().Be(Palette.Blue);

    [Fact]
    public void ReferenceHex_round_trips_through_the_parser()
    {
        for (var i = 0; i < Palette.Count; i++)
        {
            var hex = Palette.ReferenceHex(i);
            hex.Should().MatchRegex("^#[0-9A-F]{6}$");
            Palette.NearestTo(hex).Should().Be(i);
        }
    }
}
