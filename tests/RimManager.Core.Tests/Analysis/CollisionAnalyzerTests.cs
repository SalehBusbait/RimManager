using FluentAssertions;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Analysis;

public sealed class CollisionAnalyzerTests
{
    private static InMemoryFileSystem Fs() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-07-25T00:00:00Z")));

    private static Mod Mod(string id) =>
        new() { PackageId = ModId.From(id), Name = id, Source = ModSource.Workshop, RootPath = "/" + id };

    // --- progress -----------------------------------------------------------

    /// <summary>
    /// Each analyzer ticks once per mod, which is what lets the load state draw a real
    /// fraction instead of a moving stripe. Every cheap analyzer funnels through one loop in
    /// <c>Collisions.Detect</c>, so one hook serves all three — and this pins that the hook
    /// fires for a mod that claims nothing, since most mods claim nothing and a bar that only
    /// advanced on collisions would sit still through the whole scan.
    /// </summary>
    [Theory]
    [InlineData("def")]
    [InlineData("patch")]
    [InlineData("texture")]
    public void Each_analyzer_ticks_once_per_mod(string which)
    {
        var fs = Fs();
        fs.AddFile("/a/Defs/things.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");
        // b and c claim nothing at all — no Defs, no Patches, no Textures.
        fs.CreateDirectory("/b");
        fs.CreateDirectory("/c");

        var mods = new[] { Mod("a"), Mod("b"), Mod("c") };
        var ticks = 0;
        void Tick(Mod _) => ticks++;

        _ = which switch
        {
            "def" => DefCollisionAnalyzer.Analyze(mods, fs, null, Tick).ToList(),
            "patch" => PatchCollisionAnalyzer.Analyze(mods, fs, null, Tick).ToList(),
            _ => TextureCollisionAnalyzer.Analyze(mods, fs, null, Tick).ToList(),
        };

        ticks.Should().Be(3,
            "the bar counts mods examined, not mods that turned out to collide");
    }

    /// <summary>Without a hook the analyzers behave exactly as before — it is optional.</summary>
    [Fact]
    public void The_progress_hook_is_optional()
    {
        var fs = Fs();
        fs.AddFile("/a/Defs/things.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");
        fs.AddFile("/b/Defs/things.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");

        DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).Should().ContainSingle();
    }

    // --- Def overrides ------------------------------------------------------

    [Fact]
    public void Def_override_flags_same_defname_with_last_as_winner()
    {
        var fs = Fs();
        fs.AddFile("/a/Defs/things.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");
        fs.AddFile("/b/Defs/things.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");

        var conflicts = DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).ToList();

        conflicts.Should().ContainSingle();
        conflicts[0].Kind.Should().Be(ConflictKind.DefOverride);
        conflicts[0].Key.Should().Be("ThingDef/Steel");
        conflicts[0].Winner.Should().Be(ModId.From("b"), "later in load order wins");
    }

    [Fact]
    public void Different_def_types_with_same_name_do_not_collide()
    {
        var fs = Fs();
        fs.AddFile("/a/Defs/x.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");
        fs.AddFile("/b/Defs/x.xml", "<Defs><ThoughtDef><defName>Steel</defName></ThoughtDef></Defs>");

        DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).Should().BeEmpty();
    }

    [Fact]
    public void Abstract_defs_without_defname_are_ignored()
    {
        var fs = Fs();
        fs.AddFile("/a/Defs/x.xml", "<Defs><ThingDef Name=\"BaseThing\" Abstract=\"True\"><description>x</description></ThingDef></Defs>");
        fs.AddFile("/b/Defs/x.xml", "<Defs><ThingDef Name=\"BaseThing\" Abstract=\"True\"><description>y</description></ThingDef></Defs>");

        DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).Should().BeEmpty();
    }

    // --- textures -----------------------------------------------------------

    [Fact]
    public void Texture_collision_is_case_insensitive_on_the_relative_path()
    {
        var fs = Fs();
        fs.AddFile("/a/Textures/Things/Item/Steel.png", "img");
        fs.AddFile("/b/Textures/things/item/steel.png", "img");

        var conflicts = TextureCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).ToList();

        conflicts.Should().ContainSingle(c => c.Kind == ConflictKind.TextureCollision);
        conflicts[0].Key.Should().Be("things/item/steel.png");
    }

    // --- XML patches --------------------------------------------------------

    [Fact]
    public void Patch_collision_flags_same_xpath_target()
    {
        var fs = Fs();
        const string patch = "<Patch><Operation Class=\"PatchOperationReplace\"><xpath>/Defs/ThingDef[defName=\"Steel\"]/description</xpath></Operation></Patch>";
        fs.AddFile("/a/Patches/p.xml", patch);
        fs.AddFile("/b/Patches/p.xml", patch);

        var conflicts = PatchCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, null).ToList();

        conflicts.Should().ContainSingle(c => c.Kind == ConflictKind.PatchCollision);
        conflicts[0].Mods.Should().Equal(ModId.From("a"), ModId.From("b"));
    }

    [Fact]
    public void Honours_loadfolders_for_where_defs_live()
    {
        // Def is only under a LoadFolders-mapped "Current" folder, not the root.
        var fs = Fs();
        fs.AddFile("/a/LoadFolders.xml", "<loadFolders><v1.6><li>/</li><li>Current</li></v1.6></loadFolders>");
        fs.AddFile("/a/Current/Defs/x.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");
        fs.AddFile("/b/Defs/x.xml", "<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>");

        DefCollisionAnalyzer.Analyze([Mod("a"), Mod("b")], fs, "1.6")
            .Should().ContainSingle(c => c.Key == "ThingDef/Steel");
    }
}
