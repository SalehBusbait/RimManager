using System.Collections.Immutable;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The harmless rule — the one filter every conflict surface applies (the ⚡ marks,
/// the per-mod window, and formerly the 2c tab, whose grouping went with it in N6c).
/// </summary>
public sealed class ConflictsGroupingTests
{
    private static ModId Id(string v) => ModId.From(v);

    private static ModConflict Conflict(
        ConflictKind kind, string key, params (string mod, string? xml)[] providers) =>
        new(kind, key,
            [.. providers.Select(p => Id(p.mod))],
            Id(providers[^1].mod),
            Providers: [.. providers.Select(p => new ConflictProvider(Id(p.mod), "F.xml", p.xml))]);

    /// <summary>
    /// Identical markup from every provider is a real overlap that changes nothing.
    /// On the design's example install these outnumber the decisions 214 to 38.
    /// </summary>
    [Fact]
    public void Providers_shipping_identical_markup_are_harmless()
    {
        var same = Conflict(ConflictKind.DefOverride, "Gun",
            ("a.one", "<Def><A>1</A></Def>"), ("b.two", "<Def><A>1</A></Def>"));

        ConflictsPresenter.IsHarmless(same).Should().BeTrue();
    }

    [Fact]
    public void Whitespace_alone_does_not_make_two_providers_differ()
    {
        var same = Conflict(ConflictKind.DefOverride, "Gun",
            ("a.one", "<Def>\n  <A>1</A>\n</Def>"), ("b.two", "<Def>\r\n<A>1</A>\r\n</Def>"));

        ConflictsPresenter.IsHarmless(same).Should().BeTrue();
    }

    [Fact]
    public void A_real_difference_is_never_harmless()
    {
        var differs = Conflict(ConflictKind.DefOverride, "Gun",
            ("a.one", "<Def><A>1</A></Def>"), ("b.two", "<Def><A>2</A></Def>"));

        ConflictsPresenter.IsHarmless(differs).Should().BeFalse();
    }

    /// <summary>
    /// "We could not capture the XML" must never render as "nothing to see here" — a
    /// texture collision or an unreadable file is exactly the case where hiding the
    /// row would lose a real decision.
    /// </summary>
    [Fact]
    public void A_provider_with_no_captured_xml_is_never_harmless()
    {
        var unknown = Conflict(ConflictKind.TextureCollision, "Things/Steel",
            ("a.one", null), ("b.two", null));

        ConflictsPresenter.IsHarmless(unknown).Should().BeFalse();
    }
}
