using FluentAssertions;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Analysis;

/// <summary>
/// Per-provider retention in the conflict scan — the raw material for the two-up
/// XML diff viewer (3c). Without it the Conflicts tab could only say "four mods
/// define Gun_AssaultRifle"; with it, the user can see what actually differs, which
/// is what they need in order to decide.
/// </summary>
public sealed class ConflictProviderTests
{
    private static InMemoryFileSystem Fs() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-07-25T00:00:00Z")));

    private static Mod Mod(string id) =>
        new() { PackageId = ModId.From(id), Name = id, Source = ModSource.Workshop, RootPath = "/" + id };

    [Fact]
    public void Each_provider_is_recorded_in_load_order_with_its_source_file()
    {
        var fs = Fs();
        fs.AddFile("/a/Defs/guns.xml",
            "<Defs><ThingDef><defName>Gun</defName><Mass>3.5</Mass></ThingDef></Defs>");
        fs.AddFile("/b/Defs/guns.xml",
            "<Defs><ThingDef><defName>Gun</defName><Mass>3.9</Mass></ThingDef></Defs>");

        var conflict = DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).Single();

        conflict.ProvidersOrEmpty.Select(p => p.ModId.Value).Should().Equal("a", "b");
        conflict.ProvidersOrEmpty[0].SourceFile.Should().EndWith("guns.xml");
    }

    /// <summary>
    /// The contested ELEMENT is retained, not the whole file: a Defs file runs to
    /// thousands of lines and only this element is in contention.
    /// </summary>
    [Fact]
    public void The_retained_xml_is_the_contested_element_only()
    {
        var fs = Fs();
        fs.AddFile("/a/Defs/guns.xml",
            "<Defs><ThingDef><defName>Gun</defName><Mass>3.5</Mass></ThingDef>"
            + "<ThingDef><defName>Unrelated</defName></ThingDef></Defs>");
        fs.AddFile("/b/Defs/guns.xml",
            "<Defs><ThingDef><defName>Gun</defName><Mass>3.9</Mass></ThingDef></Defs>");

        var conflict = DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).Single();

        var xml = conflict.ProvidersOrEmpty[0].Xml!;
        xml.Should().Contain("Gun").And.Contain("3.5");
        xml.Should().NotContain("Unrelated", "only the contested element is kept");
    }

    /// <summary>The two sides the diff viewer opens with: the winner and the last
    /// provider it overwrote.</summary>
    [Fact]
    public void DiffPair_is_the_winner_and_the_one_it_overwrote()
    {
        var fs = Fs();
        foreach (var (mod, mass) in new[] { ("a", "1"), ("b", "2"), ("c", "3") })
        {
            fs.AddFile($"/{mod}/Defs/guns.xml",
                $"<Defs><ThingDef><defName>Gun</defName><Mass>{mass}</Mass></ThingDef></Defs>");
        }

        var conflict = DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b"), Mod("c")], fs, null).Single();
        var pair = conflict.DiffPair();

        pair.Should().NotBeNull();
        pair!.Value.Overwritten.ModId.Value.Should().Be("b");
        pair.Value.Wins.ModId.Value.Should().Be("c");
        pair.Value.Wins.ModId.Should().Be(conflict.Winner);
    }

    /// <summary>Texture collisions work on paths, so there is nothing to diff — the
    /// viewer must not be offered.</summary>
    [Fact]
    public void DiffPair_is_null_when_providers_carry_no_xml()
    {
        var conflict = new ModConflict(
            ConflictKind.TextureCollision, "Things/Steel", [ModId.From("a"), ModId.From("b")],
            ModId.From("b"),
            Providers: [new ConflictProvider(ModId.From("a")), new ConflictProvider(ModId.From("b"))]);

        conflict.DiffPair().Should().BeNull();
    }

    /// <summary>A conflict built without providers must not need a null guard at
    /// every call site.</summary>
    [Fact]
    public void ProvidersOrEmpty_is_safe_on_a_conflict_built_without_them() =>
        new ModConflict(ConflictKind.DefOverride, "k", [ModId.From("a")], ModId.From("a"))
            .ProvidersOrEmpty.Should().BeEmpty();
}
