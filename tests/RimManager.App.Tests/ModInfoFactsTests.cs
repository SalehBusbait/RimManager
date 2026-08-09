using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>O3 · the two facts Mod Info gained: size on disk, and whether the
/// description's four-line clamp is cutting anything.</summary>
public class ModInfoFactsTests
{
    [Theory]
    [InlineData(0, "1 KB")]
    [InlineData(400, "1 KB")]
    [InlineData(1_024, "1 KB")]
    [InlineData(933_888, "912 KB")]
    [InlineData(1_048_576, "1 MB")]
    [InlineData(257_949_696, "246 MB")]
    [InlineData(1_073_741_824, "1 GB")]
    [InlineData(3_650_722_201, "3.4 GB")]
    public void Sizes_read_the_way_a_file_manager_reports_them(long bytes, string expected) =>
        ByteSize.Format(bytes).Should().Be(expected);

    [Fact]
    public void A_file_that_exists_never_rounds_away_to_nothing()
    {
        // "0 KB" would read as a failure to measure rather than a small file.
        ByteSize.Format(1).Should().Be("1 KB");
    }

    [Fact]
    public void A_short_description_is_not_clamped()
    {
        DescriptionClamp.IsClamped("Adds a hat.").Should().BeFalse();
    }

    [Fact]
    public void Nothing_at_all_is_not_clamped()
    {
        DescriptionClamp.IsClamped(null).Should().BeFalse();
        DescriptionClamp.IsClamped("   ").Should().BeFalse();
    }

    [Fact]
    public void A_long_paragraph_is_clamped()
    {
        DescriptionClamp.IsClamped(new string('x', 400)).Should().BeTrue();
    }

    [Fact]
    public void Blank_lines_between_paragraphs_still_count_as_lines()
    {
        // Five short paragraphs are five lines even though no line is full — the
        // clamp counts rendered lines, not characters.
        DescriptionClamp.IsClamped("a\nb\nc\nd\ne").Should().BeTrue();
        DescriptionClamp.IsClamped("a\nb\nc\nd").Should().BeFalse();
    }

    [Fact]
    public void Windows_line_endings_do_not_inflate_the_count()
    {
        DescriptionClamp.IsClamped("a\r\nb\r\nc\r\nd").Should().BeFalse();
    }

    [Fact]
    public void The_description_viewer_footer_counts_words_and_characters()
    {
        DescriptionViewerViewModel.Footer("one two three").Should().Be("3 words · 13 characters");
    }

    [Fact]
    public void The_footer_says_word_not_words_for_one()
    {
        DescriptionViewerViewModel.Footer("hat").Should().Be("1 word · 3 characters");
    }

    [Fact]
    public void An_empty_description_says_so_rather_than_claiming_zero_words()
    {
        DescriptionViewerViewModel.Footer("").Should().Be("empty");
    }
}
