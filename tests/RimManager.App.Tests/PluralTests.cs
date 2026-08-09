using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>D3 · the house style for counted nouns.</summary>
public class PluralTests
{
    [Theory]
    [InlineData(0, "0 mods")]
    [InlineData(1, "1 mod")]
    [InlineData(2, "2 mods")]
    public void One_is_singular_and_everything_else_is_not(int count, string expected) =>
        Plural.Of(count, "mod").Should().Be(expected);

    [Fact]
    public void Zero_is_plural_because_English_says_so()
    {
        // "0 mod" is the mistake the naive (n > 1) test makes.
        Plural.Of(0, "snapshot").Should().Be("0 snapshots");
    }

    [Fact]
    public void An_irregular_noun_can_give_both_forms()
    {
        Plural.Of(1, "entry", "entries").Should().Be("1 entry");
        Plural.Of(3, "entry", "entries").Should().Be("3 entries");
    }

    /// <summary>
    /// The specific contradiction D3 was filed for: the commit bar's blocked path and its
    /// overridden path write into the same slot about the same noun, and one of them was
    /// saying "1 blocking warning(s)".
    /// </summary>
    [Fact]
    public void The_commit_bars_two_paths_agree_about_one_blocking_warning()
    {
        var blocked = $"{Plural.Of(1, "blocking warning")} would leave the game unable to load.";
        var overridden = ApplyConcerns.For(DriftKind.InSync, blockingErrors: 1);

        blocked.Should().StartWith("1 blocking warning ");
        blocked.Should().NotContain("(s)");
        overridden.Should().ContainSingle()
            .Which.Should().StartWith("1 blocking warning ").And.NotContain("(s)");
    }
}
