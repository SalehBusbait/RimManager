using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Scanning;

public sealed class ModScannerTests
{
    private static InMemoryFileSystem Fs() =>
        new(new FixedClock(DateTimeOffset.Parse("2026-07-24T00:00:00Z")));

    private static void AddMod(InMemoryFileSystem fs, string folder, string packageId, string? name = null)
    {
        fs.AddFile($"{folder}/About/About.xml",
            $"<ModMetaData><packageId>{packageId}</packageId><name>{name ?? packageId}</name></ModMetaData>");
    }

    [Fact]
    public void Dedupes_same_package_across_sources_local_wins_with_warning()
    {
        var fs = Fs();
        AddMod(fs, "/ws/111", "Author.Mod");        // Workshop casing variant
        AddMod(fs, "/local/mymod", "author.mod");   // Local casing variant

        var result = new ModScanner(fs).Scan(
            [new ModSourceRoot("/ws", ModSource.Workshop), new ModSourceRoot("/local", ModSource.Local)]);

        result.Mods.Should().ContainSingle();
        result.Mods[0].Source.Should().Be(ModSource.Local, "local overrides workshop");
        result.Warnings.Should().ContainSingle(w => w.Code == "duplicate.packageId");
    }

    [Fact]
    public void Refines_core_vs_dlc_by_package_id()
    {
        var fs = Fs();
        AddMod(fs, "/data/Core", "Ludeon.RimWorld");
        AddMod(fs, "/data/Royalty", "Ludeon.RimWorld.Royalty");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/data", ModSource.Core)]);

        result.ById[ModId.From("ludeon.rimworld")].Source.Should().Be(ModSource.Core);
        result.ById[ModId.From("ludeon.rimworld.royalty")].Source.Should().Be(ModSource.Dlc);
    }

    /// <summary>
    /// Ludeon's own About.xml files carry NO &lt;name&gt; — verified on the real
    /// install for Core and all five expansions. The packageId fallback therefore put
    /// "Ludeon.RimWorld.Royalty" on the row that anchors every load order.
    /// <para>
    /// The About.xml here is deliberately built by hand rather than through
    /// <c>AddMod</c>, because <c>AddMod</c> always writes a name and so cannot
    /// reproduce the shape that caused this.
    /// </para>
    /// </summary>
    [Fact]
    public void Core_and_dlc_are_named_even_though_their_about_xml_has_no_name()
    {
        var fs = Fs();
        fs.AddFile("/data/Core/About/About.xml",
            "<ModMetaData><packageId>Ludeon.RimWorld</packageId></ModMetaData>");
        fs.AddFile("/data/Royalty/About/About.xml",
            "<ModMetaData><packageId>Ludeon.RimWorld.Royalty</packageId></ModMetaData>");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/data", ModSource.Core)]);

        result.ById[ModId.From("ludeon.rimworld")].Name.Should().Be("RimWorld");
        result.ById[ModId.From("ludeon.rimworld.royalty")].Name.Should().Be("Royalty");
    }

    /// <summary>
    /// The mapping is a FALLBACK, never an override. If Ludeon ever ships a name, it
    /// is the author's and wins — the same precedence every other mod gets.
    /// </summary>
    [Fact]
    public void A_name_in_about_xml_still_beats_the_known_mod_fallback()
    {
        var fs = Fs();
        AddMod(fs, "/data/Core", "Ludeon.RimWorld", name: "RimWorld (Core)");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/data", ModSource.Core)]);

        result.ById[ModId.From("ludeon.rimworld")].Name.Should().Be("RimWorld (Core)");
    }

    /// <summary>
    /// <c>ModSource.Git</c> had no producer at all: the scanner is handed one root per
    /// source and there is no "git root", because a clone lives in
    /// <c>&lt;game&gt;/Mods</c> in exactly the place a hand-copied folder does. Every
    /// consumer already read the value — precedence, the row badge, the source filter,
    /// the <c>.rwlist</c> mapping — so the G badge could never render for anyone.
    /// <para>
    /// Measured on a real Mods directory: 39 of 55 folders are clones with GitHub
    /// remotes, and all 39 were reported as Local.
    /// </para>
    /// </summary>
    [Fact]
    public void A_local_mod_holding_a_git_directory_is_a_git_source()
    {
        var fs = Fs();
        AddMod(fs, "/local/clone", "author.clone");
        fs.AddFile("/local/clone/.git/HEAD", "ref: refs/heads/main");
        AddMod(fs, "/local/copied", "author.copied");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/local", ModSource.Local)]);

        result.ById[ModId.From("author.clone")].Source.Should().Be(ModSource.Git);
        result.ById[ModId.From("author.copied")].Source.Should().Be(ModSource.Local);
    }

    /// <summary>
    /// The restriction that makes the probe safe. A <c>.git</c> inside a Workshop
    /// folder is upload residue from the author's machine — 33 of a real 405-mod
    /// library have one, none of them a repository the user owns. Classifying those as
    /// Git would light the ⎇ glyph on mods nobody touched and point "fetch on startup"
    /// at 33 strangers' upstreams, which is exactly the bug R6 fixed once already.
    /// </summary>
    [Fact]
    public void A_git_directory_inside_a_workshop_folder_is_residue_and_changes_nothing()
    {
        var fs = Fs();
        AddMod(fs, "/ws/111", "author.published");
        fs.AddFile("/ws/111/.git/HEAD", "ref: refs/heads/main");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/ws", ModSource.Workshop)]);

        result.ById[ModId.From("author.published")].Source.Should().Be(ModSource.Workshop);
    }

    // The vault's two scanner tests stood here — a .git inside a vault copy not
    // demoting it, and the vault copy beating the Workshop one. Both went with the
    // vault itself (O13); there is no ModSource.Pinned to assert about any more.

    /// <summary>
    /// A <c>.git</c> FILE is a worktree or submodule pointer, and the one this project
    /// met pointed at <c>C:/gits/HFM.git</c> on someone else's disk. Zero of the 55
    /// real local folders have one, so the cheap and honest rule is: directory only.
    /// </summary>
    [Fact]
    public void A_git_file_rather_than_a_directory_is_not_a_clone()
    {
        var fs = Fs();
        AddMod(fs, "/local/pointer", "author.pointer");
        fs.AddFile("/local/pointer/.git", "gitdir: C:/gits/HFM.git");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/local", ModSource.Local)]);

        result.ById[ModId.From("author.pointer")].Source.Should().Be(ModSource.Local);
    }

    /// <summary>
    /// A clone still beats the Workshop copy of the same mod. Git sits below Local and
    /// above Workshop in the precedence table, so which FOLDER wins is unchanged by the
    /// relabelling — only the word on the badge changes.
    /// </summary>
    [Fact]
    public void A_clone_still_wins_over_the_workshop_copy_of_the_same_mod()
    {
        var fs = Fs();
        AddMod(fs, "/ws/111", "Author.Mod");
        AddMod(fs, "/local/clone", "author.mod");
        fs.AddFile("/local/clone/.git/HEAD", "ref: refs/heads/main");

        var result = new ModScanner(fs).Scan(
            [new ModSourceRoot("/ws", ModSource.Workshop), new ModSourceRoot("/local", ModSource.Local)]);

        result.Mods.Should().ContainSingle();
        result.Mods[0].Source.Should().Be(ModSource.Git);
        result.Mods[0].RootPath.Should().Contain("clone");
    }

    [Fact]
    public void Folder_without_about_is_skipped_with_an_info_warning()
    {
        var fs = Fs();
        AddMod(fs, "/ws/111", "good.mod");
        fs.AddFile("/ws/stray/readme.txt", "not a mod");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/ws", ModSource.Workshop)]);

        result.Mods.Should().ContainSingle(m => m.PackageId == ModId.From("good.mod"));
        result.Warnings.Should().Contain(w => w.Code == "scan.no-about");
    }

    [Fact]
    public void Missing_package_id_gets_a_synthetic_id_and_surfaces_the_error()
    {
        var fs = Fs();
        fs.AddFile("/ws/broken/About/About.xml", "<ModMetaData><name>Broken</name></ModMetaData>");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/ws", ModSource.Workshop)]);

        var mod = result.Mods.Should().ContainSingle().Subject;
        mod.PackageId.Value.Should().StartWith("unknown.");
        mod.HasErrors.Should().BeTrue();
        mod.Warnings.Should().Contain(w => w.Code == "about.missing-packageId");
    }

    [Fact]
    public void Reads_published_file_id_when_present()
    {
        var fs = Fs();
        AddMod(fs, "/ws/2009463077", "brrainz.harmony");
        fs.AddFile("/ws/2009463077/About/PublishedFileId.txt", "2009463077\n");

        var result = new ModScanner(fs).Scan([new ModSourceRoot("/ws", ModSource.Workshop)]);

        result.Mods[0].PublishedFileId.Should().Be("2009463077");
    }

    [Fact]
    public void Missing_source_root_is_skipped_not_thrown()
    {
        var act = () => new ModScanner(Fs()).Scan([new ModSourceRoot("/does/not/exist", ModSource.Workshop)]);
        act.Should().NotThrow();
    }
}
