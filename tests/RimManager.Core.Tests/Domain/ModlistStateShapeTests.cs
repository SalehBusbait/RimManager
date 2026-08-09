using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

public sealed class ModlistStateShapeTests
{
    [Fact]
    public void Active_mod_ids_skip_disabled_and_separators()
    {
        var state = ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Separator("sep-1", "Frameworks"),
            ModlistEntry.Mod(ModId.From("a.enabled"), enabled: true),
            ModlistEntry.Mod(ModId.From("b.disabled"), enabled: false),
            ModlistEntry.Mod(ModId.From("c.enabled"), enabled: true),
        ]);

        state.ActiveModIds().Select(id => id.Value).Should().Equal("a.enabled", "c.enabled");
        state.AllModIds().Select(id => id.Value).Should().Equal("a.enabled", "b.disabled", "c.enabled");
    }

    [Fact]
    public void Mod_factory_uses_canonical_id_and_display()
    {
        var entry = ModlistEntry.Mod(ModId.From("Brrainz.Harmony"));
        entry.Kind.Should().Be(ModlistEntryKind.Mod);
        entry.Id.Should().Be("brrainz.harmony");
        entry.DisplayName.Should().Be("Brrainz.Harmony");
        entry.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Separator_factory_carries_palette_index_and_collapsed()
    {
        // Colour is a palette INDEX, never a hex string (non-negotiable #6), so it
        // resolves through the active theme and stays legible in light and dark.
        var sep = ModlistEntry.Separator("sep-1", "QoL", Palette.Violet, collapsed: true);
        sep.Kind.Should().Be(ModlistEntryKind.Separator);
        sep.PaletteIndex.Should().Be(Palette.Violet);
        sep.Collapsed.Should().BeTrue();
    }
}
