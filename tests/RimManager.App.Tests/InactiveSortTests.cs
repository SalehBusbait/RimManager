using System;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Ordering for the inactive pane. "Sorted" is a claim a screenshot cannot check — a
/// nearly-sorted list looks exactly like a sorted one — and the tie-breaking is what
/// stops the pane reshuffling under the pointer on every rescan.
/// </summary>
public sealed class InactiveSortTests
{
    private static ModRowViewModel Row(
        string name, ModSource source = ModSource.Workshop,
        string? version = null, string? author = null) =>
        new(new Mod
        {
            PackageId = ModId.From($"pkg.{name.ToLowerInvariant()}"),
            Name = name,
            Source = source,
            RootPath = $"/mods/{name}",
            ModVersion = version,
            Authors = author is null ? [] : [author],
        });

    [Fact]
    public void Name_is_the_default_and_is_case_insensitive()
    {
        var rows = new RowViewModel[] { Row("zeta"), Row("Alpha"), Row("beta") };

        InactiveSort.Apply(rows, InactiveSortKey.Name, ascending: true)
            .Cast<ModRowViewModel>().Select(r => r.Name)
            .Should().Equal("Alpha", "beta", "zeta");
    }

    [Fact]
    public void Descending_reverses_the_key_but_not_the_tie_break()
    {
        var rows = new RowViewModel[] { Row("Alpha"), Row("beta"), Row("zeta") };

        InactiveSort.Apply(rows, InactiveSortKey.Name, ascending: false)
            .Cast<ModRowViewModel>().Select(r => r.Name)
            .Should().Equal("zeta", "beta", "Alpha");
    }

    /// <summary>
    /// Every key falls through to the name, so equal rows keep a fixed order. Without
    /// it a rescan could reorder the pane under the pointer while the user is reaching
    /// for a row.
    /// </summary>
    [Fact]
    public void Rows_that_tie_on_the_key_are_ordered_by_name()
    {
        var rows = new RowViewModel[]
        {
            Row("Zeta", ModSource.Local),
            Row("Alpha", ModSource.Local),
            Row("Mid", ModSource.Local),
        };

        InactiveSort.Apply(rows, InactiveSortKey.Source, ascending: true)
            .Cast<ModRowViewModel>().Select(r => r.Name)
            .Should().Equal("Alpha", "Mid", "Zeta");
    }

    [Fact]
    public void Sorting_is_idempotent()
    {
        var rows = new RowViewModel[] { Row("c"), Row("a"), Row("b") };

        var once = InactiveSort.Apply(rows, InactiveSortKey.Name, true);
        var twice = InactiveSort.Apply(once, InactiveSortKey.Name, true);

        twice.Cast<ModRowViewModel>().Select(r => r.Name)
            .Should().Equal(once.Cast<ModRowViewModel>().Select(r => r.Name));
    }

    [Fact]
    public void Missing_values_sort_together_rather_than_throwing()
    {
        var rows = new RowViewModel[]
        {
            Row("HasVersion", version: "1.2.0"),
            Row("NoVersion"),
            Row("AlsoNone"),
        };

        var sorted = InactiveSort.Apply(rows, InactiveSortKey.Version, ascending: true)
            .Cast<ModRowViewModel>().Select(r => r.Name).ToArray();

        sorted.Should().HaveCount(3);
        sorted.Should().Contain(["HasVersion", "NoVersion", "AlsoNone"]);
    }

    [Fact]
    public void Author_sorts_on_the_first_declared_author()
    {
        var rows = new RowViewModel[]
        {
            Row("One", author: "Zed"),
            Row("Two", author: "Ada"),
        };

        InactiveSort.Apply(rows, InactiveSortKey.Author, ascending: true)
            .Cast<ModRowViewModel>().Select(r => r.Name)
            .Should().Equal("Two", "One");
    }

    /// <summary>Separators never reach the inactive pane, but the sort must not choke
    /// if one is passed — it filters to mods rather than casting blindly.</summary>
    [Fact]
    public void Non_mod_rows_are_dropped_rather_than_crashing()
    {
        ImmutableArray<RowViewModel> rows = [Row("Alpha")];

        InactiveSort.Apply(rows, InactiveSortKey.Name, true).Should().ContainSingle();
    }

    [Fact]
    public void Every_key_has_a_label_for_the_header()
    {
        foreach (var key in Enum.GetValues<InactiveSortKey>())
            InactiveSort.Label(key).Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A regression, caught by reading rather than by a failing test: N1 turned
    /// <c>ModRowViewModel.Source</c> into the badge's TOOLTIP ("Workshop — subscribed
    /// through Steam") when the badge became a wordless icon, and this sort was still
    /// reading it. The grouping survived by luck — every description happens to start
    /// with its own label — but a column must order by what it DISPLAYS, or the next
    /// wording change silently reorders the list.
    /// </summary>
    [Fact]
    public void Source_orders_by_the_short_label_not_by_the_tooltip_sentence()
    {
        var rows = new RowViewModel[]
        {
            Row("w", ModSource.Workshop), Row("c", ModSource.Core),
            Row("l", ModSource.Local), Row("d", ModSource.Dlc),
        };

        var sorted = InactiveSort.Apply(rows, InactiveSortKey.Source, ascending: true)
            .Cast<ModRowViewModel>().ToArray();

        sorted.Select(r => r.SourceLabel).Should().Equal("Core", "DLC", "Local", "Workshop");

        // The value being ordered is the one drawn in the column, not the sentence.
        sorted.Should().OnlyContain(r => !r.SourceLabel.Contains(' '));
    }

    /// <summary>
    /// PackageId became a sort key when the inactive pane gained a PACKAGEID column
    /// (N1): every column it draws is clickable, because the one that is not reads as
    /// broken rather than as deliberate.
    /// </summary>
    [Fact]
    public void PackageId_is_sortable_because_the_pane_now_draws_that_column()
    {
        var rows = new RowViewModel[] { Row("zeta"), Row("alpha"), Row("mid") };

        InactiveSort.Apply(rows, InactiveSortKey.PackageId, ascending: true)
            .Cast<ModRowViewModel>().Select(r => r.PackageIdText)
            .Should().Equal("pkg.alpha", "pkg.mid", "pkg.zeta");
    }

    /// <summary>
    /// The heading carries the arrow only on the sorted column, and the caption matches
    /// the ACTIVE pane's legend word for word where the two panes share a column — two
    /// panes calling one column two names is worse than an abbreviation.
    /// </summary>
    [Fact]
    public void Only_the_sorted_column_heading_carries_an_arrow()
    {
        InactiveSort.Header(InactiveSortKey.Name, InactiveSortKey.Name, ascending: true)
            .Should().Be("NAME ▲");
        InactiveSort.Header(InactiveSortKey.Name, InactiveSortKey.Name, ascending: false)
            .Should().Be("NAME ▼");
        InactiveSort.Header(InactiveSortKey.Name, InactiveSortKey.Version, ascending: true)
            .Should().Be("NAME");
    }

    [Theory]
    [InlineData(InactiveSortKey.Source, "SRC")]
    [InlineData(InactiveSortKey.Name, "NAME")]
    [InlineData(InactiveSortKey.PackageId, "PACKAGEID")]
    [InlineData(InactiveSortKey.Author, "AUTHOR")]
    [InlineData(InactiveSortKey.Version, "VER")]
    public void Every_column_has_a_caption_and_none_is_blank(InactiveSortKey key, string caption)
    {
        InactiveSort.Header(key, InactiveSortKey.Name, ascending: true)
            .Should().StartWith(caption);
    }

    /// <summary>
    /// Every key the enum offers must be reachable from a heading, or a sort exists
    /// that nothing can invoke — the inverse of a control that does nothing, and just
    /// as invisible.
    /// </summary>
    [Fact]
    public void Every_sort_key_has_a_heading_of_its_own()
    {
        var captions = Enum.GetValues<InactiveSortKey>()
            .Select(k => InactiveSort.Header(k, InactiveSortKey.Name, ascending: true))
            .Select(h => h.Split(' ')[0])
            .ToArray();

        captions.Should().OnlyHaveUniqueItems();
        captions.Should().OnlyContain(c => c.Length > 0);
    }
}
