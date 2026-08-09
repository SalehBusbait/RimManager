using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sharing;
using Xunit;

namespace RimManager.Core.Tests.Sharing;

/// <summary>
/// A Workshop-borne <c>.rwlist</c> becoming a NEW modlist's arrangement (NF-10,
/// T7 decision 3: the current list is never touched).
/// </summary>
public sealed class RwListWorkshopImportTests
{
    [Fact]
    public void Entries_are_reproduced_verbatim_with_identity_kept()
    {
        var list = new RwList
        {
            Name = "Combat Overhaul",
            Entries =
            [
                RwEntry.Separator("s1", "CORE", paletteIndex: 2, collapsed: true),
                new RwEntry
                {
                    Type = RwEntryKind.Mod,
                    PackageId = "CETeam.CombatExtended",
                    DisplayName = "Combat Extended",
                    Source = RwSource.Workshop,
                    PublishedFileId = "2890901044",
                    ModVersion = "5.4",
                },
                new RwEntry
                {
                    Type = RwEntryKind.Mod,
                    PackageId = "some.gitmod",
                    Source = RwSource.Git,
                    GitUrl = "https://example.com/mod.git",
                    GitRef = "main",
                },
            ],
        };

        var state = RwListWorkshopImport.ToState(list);

        state.Entries.Should().HaveCount(3);

        var separator = state.Entries[0];
        separator.Kind.Should().Be(ModlistEntryKind.Separator);
        separator.DisplayName.Should().Be("CORE");
        separator.PaletteIndex.Should().Be(2);
        separator.Collapsed.Should().BeTrue();

        var ce = state.Entries[1];
        ce.Id.Should().Be("ceteam.combatextended", "packageIds route through ModId");
        ce.DisplayName.Should().Be("Combat Extended");
        ce.Source.Should().Be(ModSource.Workshop);
        ce.PublishedFileId.Should().Be("2890901044");
        ce.ModVersion.Should().Be("5.4");

        var git = state.Entries[2];
        git.Source.Should().Be(ModSource.Git);
        git.GitUrl.Should().Be("https://example.com/mod.git");
        git.GitRef.Should().Be("main");
        git.DisplayName.Should().Be("some.gitmod", "no display name falls back to the id");
    }

    /// <summary>A mod entry with no packageId can be addressed by nothing downstream.</summary>
    [Fact]
    public void A_mod_entry_without_a_package_id_is_dropped_not_guessed()
    {
        var list = new RwList
        {
            Entries = [new RwEntry { Type = RwEntryKind.Mod, DisplayName = "??" }],
        };

        RwListWorkshopImport.ToState(list).Entries.Should().BeEmpty();
    }

    [Fact]
    public void The_name_comes_from_the_list_then_the_file_and_never_collides()
    {
        RwListWorkshopImport.UniqueName("Combat Overhaul", "x.rwlist", [])
            .Should().Be("Combat Overhaul");

        RwListWorkshopImport.UniqueName(null, "My Colony List.rwlist", [])
            .Should().Be("My Colony List");

        RwListWorkshopImport.UniqueName("Combat Overhaul", "x.rwlist",
                ["Combat Overhaul", "Combat Overhaul (2)"])
            .Should().Be("Combat Overhaul (3)");
    }
}
