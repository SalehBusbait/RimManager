using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>One row of Settings ▸ Modlists.</summary>
public sealed class ModlistRowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private static Modlist Given(
        string name = "Heavily modded",
        bool isDefault = false,
        bool captures = false,
        int mods = 3,
        int separators = 1)
    {
        var entries = new List<ModlistEntry>();
        for (var i = 0; i < separators; i++)
            entries.Add(ModlistEntry.Separator($"s{i}", $"Group {i}"));
        for (var i = 0; i < mods; i++)
            entries.Add(ModlistEntry.Mod(ModId.From($"mod.{i}")));

        return new Modlist
        {
            Id = "l1",
            Name = name,
            IsDefault = isDefault,
            CapturesModSettings = captures,
            State = ModlistState.Empty.WithEntries(entries),
            LastUsedUtc = Now.AddDays(-3),
        };
    }

    [Fact]
    public void It_counts_mods_and_separators_separately()
    {
        var row = new ModlistRowViewModel(Given(mods: 5, separators: 2), 0, 0, false, Now);

        row.Mods.Should().Be(5);
        row.Separators.Should().Be(2, "a separator is not a mod and must not inflate the count");
    }

    /// <summary>
    /// Zero is a measurement; an absence is not. The same distinction the git
    /// commits-behind column makes between null and nought.
    /// </summary>
    [Fact]
    public void A_list_that_captures_nothing_shows_a_dash_rather_than_zero()
    {
        new ModlistRowViewModel(Given(captures: false), 0, 0, false, Now)
            .Settings.Should().Be("—");

        new ModlistRowViewModel(Given(captures: true), 0, 0, false, Now)
            .Settings.Should().Be("0", "capturing and having captured nothing yet are different");

        new ModlistRowViewModel(Given(captures: true), 0, 397, false, Now)
            .Settings.Should().Be("397");
    }

    /// <summary>"Why can I not delete this" needs an answer visible before the button is
    /// reached for.</summary>
    [Fact]
    public void The_default_and_the_open_list_both_announce_themselves()
    {
        var row = new ModlistRowViewModel(Given(isDefault: true), 0, 0, isCurrent: true, Now);

        row.IsDefault.Should().BeTrue();
        row.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void The_colour_dot_is_a_palette_index_and_is_normalised()
    {
        var wild = Given() with { PaletteIndex = 99 };

        var row = new ModlistRowViewModel(wild, 0, 0, false, Now);

        row.PaletteIndex.Should().BeInRange(0, Palette.Count - 1);
        new[] { row.IsPalette0, row.IsPalette1, row.IsPalette2,
                row.IsPalette3, row.IsPalette4, row.IsPalette5 }
            .Count(x => x).Should().Be(1, "exactly one bound class drives the swatch");
    }

    [Fact]
    public void Last_used_is_worded_by_the_presenter()
    {
        new ModlistRowViewModel(Given(), 0, 0, false, Now)
            .LastUsed.Should().Be("3 days ago");

        var never = Given() with { LastUsedUtc = null };
        new ModlistRowViewModel(never, 0, 0, false, Now).LastUsed.Should().Be("never");
    }

    [Fact]
    public void An_empty_list_is_a_legitimate_row()
    {
        var row = new ModlistRowViewModel(Given(mods: 0, separators: 0), 0, 0, false, Now);

        row.Mods.Should().Be(0);
        row.Separators.Should().Be(0);
    }
}
