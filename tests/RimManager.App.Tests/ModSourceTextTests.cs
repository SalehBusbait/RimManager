using System;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The bug this class exists to prevent: <c>ModSource.ToString()</c> was being shown
/// to the user, and the enum member is spelled <c>Dlc</c>. An identifier is not
/// display text — every expansion's row tooltip and mod-info pill read "Dlc".
/// </summary>
public sealed class ModSourceTextTests
{
    [Fact]
    public void Dlc_is_capitalised_as_an_initialism_not_as_an_identifier()
    {
        ModSourceText.Label(ModSource.Dlc).Should().Be("DLC");
        ModSourceText.Describe(ModSource.Dlc).Should().StartWith("DLC");
    }

    /// <summary>
    /// The whole enum, not a sample: a source added later must be given words
    /// deliberately, and falling through to "Unknown" is exactly the silent wrong
    /// label this replaced.
    /// </summary>
    [Theory]
    [InlineData(ModSource.Core)]
    [InlineData(ModSource.Dlc)]
    [InlineData(ModSource.Workshop)]
    [InlineData(ModSource.Local)]
    [InlineData(ModSource.Git)]
    public void Every_real_source_has_both_a_label_and_a_description(ModSource source)
    {
        ModSourceText.Label(source).Should().NotBe("Unknown");
        ModSourceText.Describe(source).Should().NotBe("Unknown source");

        // The description is the ONLY place the source is written out now that the
        // badge is a 9px icon, so it has to say more than the label repeats.
        ModSourceText.Describe(source).Length.Should()
            .BeGreaterThan(ModSourceText.Label(source).Length);
    }

    /// <summary>
    /// No two sources may share a label. Six badges tinted differently but named the
    /// same would make the tooltip useless precisely where the icon is ambiguous.
    /// </summary>
    [Fact]
    public void No_two_sources_are_given_the_same_words()
    {
        var real = Enum.GetValues<ModSource>().Where(s => s != ModSource.Unknown).ToArray();

        real.Select(ModSourceText.Label).Should().OnlyHaveUniqueItems();
        real.Select(ModSourceText.Describe).Should().OnlyHaveUniqueItems();
    }

    /// <summary>A source the scanner could not classify says so rather than lying.</summary>
    [Fact]
    public void An_unclassified_source_is_named_unknown()
    {
        ModSourceText.Label(ModSource.Unknown).Should().Be("Unknown");
        ModSourceText.Describe(ModSource.Unknown).Should().Be("Unknown source");
    }
}
