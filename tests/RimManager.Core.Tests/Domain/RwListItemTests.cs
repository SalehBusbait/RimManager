using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// What makes a Workshop item a mod-LIST item (NF-10): a <c>.rwlist</c> payload and no
/// game content — with content winning over payload, always.
/// </summary>
public sealed class RwListItemTests
{
    private static Mod With(ContentFlags content) => new()
    {
        PackageId = ModId.From("author.somelist"),
        Name = "Some List",
        Source = ModSource.Workshop,
        RootPath = "/ws/123",
        Content = content,
    };

    [Fact]
    public void A_bare_rwlist_payload_is_a_list_item() =>
        With(ContentFlags.RwList).IsRwListItem.Should().BeTrue();

    /// <summary>
    /// Content wins over payload (T7 decision 1): a folder with Defs AND a .rwlist is
    /// a mod that happens to bundle a list — treating it as a list would hide real
    /// content from the load order.
    /// </summary>
    [Theory]
    [InlineData(ContentFlags.Defs)]
    [InlineData(ContentFlags.Patches)]
    [InlineData(ContentFlags.Assemblies)]
    [InlineData(ContentFlags.Textures)]
    [InlineData(ContentFlags.Sounds)]
    public void Any_content_bearing_folder_makes_it_a_mod_not_a_list(ContentFlags content) =>
        With(ContentFlags.RwList | content).IsRwListItem.Should().BeFalse();

    /// <summary>Languages/Sources load nothing into the game and do not disqualify.</summary>
    [Fact]
    public void Languages_and_sources_do_not_disqualify() =>
        With(ContentFlags.RwList | ContentFlags.Languages | ContentFlags.Sources)
            .IsRwListItem.Should().BeTrue();

    [Fact]
    public void An_ordinary_contentless_folder_is_not_a_list_item() =>
        With(ContentFlags.None).IsRwListItem.Should().BeFalse();
}
