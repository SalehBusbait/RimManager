using System.Collections.Generic;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sharing;
using Xunit;

namespace RimManager.Core.Tests.Sharing;

public sealed class RwListTests
{
    private static Mod Mod(string id, string name, string? version = null, string? pfid = null,
        ModSource source = ModSource.Workshop) => new()
        {
            PackageId = ModId.From(id),
            Name = name,
            Source = source,
            RootPath = "/" + id,
            ModVersion = version,
            PublishedFileId = pfid,
        };

    private static RwList SampleList()
    {
        var state = ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Separator("sep-1", "Frameworks", Palette.Violet),
            ModlistEntry.Mod(ModId.From("brrainz.harmony")),
            ModlistEntry.Mod(ModId.From("author.mod")),
        ]);

        var byId = new Dictionary<ModId, Mod>
        {
            [ModId.From("brrainz.harmony")] = Mod("brrainz.harmony", "Harmony", "2.4.2.0", "2009463077"),
            [ModId.From("author.mod")] = Mod("author.mod", "Author Mod", "1.0"),
        };
        var meta = new Dictionary<ModId, ModMetadata>
        {
            [ModId.From("brrainz.harmony")] = new() { Favorite = true, Note = "first", TagIds = ["t1"] },
        };

        return RwListBuilder.Build(state, byId, meta,
            [new Tag { Id = "t1", Name = "framework", PaletteIndex = Palette.Violet }], [],
            new RwListInfo("Kitchen Sink", "me", null, "1.6", [ModId.From("ludeon.rimworld.royalty")]));
    }

    // --- serializer / checksum ---------------------------------------------

    [Fact]
    public void Serialize_then_parse_round_trips()
    {
        var original = SampleList();
        var reparsed = RwListSerializer.Parse(RwListSerializer.Serialize(original));

        reparsed.Name.Should().Be("Kitchen Sink");
        reparsed.Entries.Length.Should().Be(3);
        reparsed.Mods.Should().HaveCount(2);
        // The index is authoritative; the hex rides along as an advisory value for
        // other tools reading the file, since a bare index means nothing to them.
        reparsed.Tags.Should().ContainSingle(t => t.PaletteIndex == Palette.Violet);
        reparsed.Tags.Single().Color.Should().Be(Palette.ReferenceHex(Palette.Violet));
    }

    [Fact]
    public void Checksum_verifies_and_detects_tampering()
    {
        var parsed = RwListSerializer.Parse(RwListSerializer.Serialize(SampleList()));
        RwListSerializer.VerifyChecksum(parsed).Should().BeTrue();

        var tampered = parsed with { Name = "Hacked" };
        RwListSerializer.VerifyChecksum(tampered).Should().BeFalse("the content no longer matches the checksum");
    }

    // --- builder -----------------------------------------------------------

    [Fact]
    public void Builder_uses_positional_grouping_and_carries_metadata()
    {
        var list = SampleList();

        list.Entries[0].Type.Should().Be(RwEntryKind.Separator);
        var harmony = list.Mods.First();
        harmony.PackageId.Should().Be("brrainz.harmony");
        harmony.PublishedFileId.Should().Be("2009463077");
        harmony.Favorite.Should().BeTrue();
        harmony.TagIds.Should().Equal("t1");
        // No separatorId back-reference exists on the model at all (positional).
    }

    // --- export ------------------------------------------------------------

    [Fact]
    public void Exports_modsconfig_with_the_mods_in_order()
    {
        var xml = RwListExport.ToModsConfig(SampleList());
        xml.Should().Contain("<li>brrainz.harmony</li>");
        xml.IndexOf("brrainz.harmony", StringComparison.Ordinal)
            .Should().BeLessThan(xml.IndexOf("author.mod", StringComparison.Ordinal));
    }

    [Fact]
    public void Exports_markdown_with_headings_and_workshop_links()
    {
        var md = RwListExport.ToMarkdown(SampleList());
        md.Should().Contain("## Frameworks");
        md.Should().Contain("[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)");
    }

    [Fact]
    public void Exports_csv_rows()
    {
        var csv = RwListExport.ToCsv(SampleList());
        csv.Should().StartWith("index,packageId,name,source,version");
        csv.Should().Contain("brrainz.harmony,Harmony,Workshop,2.4.2.0");
    }

    // --- import / reconcile ------------------------------------------------

    [Fact]
    public void Imports_modsconfig_as_mod_entries()
    {
        const string xml = """
            <ModsConfigData><version>1.6.4871</version>
              <activeMods><li>a.b</li><li>c.d</li></activeMods>
              <knownExpansions><li>ludeon.rimworld.royalty</li></knownExpansions>
            </ModsConfigData>
            """;

        var list = RwListImport.Load(xml, out var ok);
        ok.Should().BeTrue();
        list.Mods.Select(m => m.PackageId).Should().Equal("a.b", "c.d");
        list.RequiredDlc.Should().Contain("ludeon.rimworld.royalty");
    }

    [Fact]
    public void Import_load_dispatches_json_vs_xml()
    {
        var json = RwListSerializer.Serialize(SampleList());
        RwListImport.Load(json, out _).Mods.Should().HaveCount(2);
    }

    [Fact]
    public void Reconcile_classifies_installed_missing_and_version_mismatch()
    {
        var list = SampleList(); // harmony 2.4.2.0, author.mod 1.0
        var installed = new Dictionary<ModId, Mod>
        {
            [ModId.From("brrainz.harmony")] = Mod("brrainz.harmony", "Harmony", "2.4.2.0"),
            [ModId.From("author.mod")] = Mod("author.mod", "Author Mod", "2.0"), // different version
        };

        var report = ImportReconciler.Reconcile(list, installed);

        report.InstalledCount.Should().Be(1);
        report.VersionMismatchCount.Should().Be(1);
        report.MissingCount.Should().Be(0);

        var mismatch = report.Items.Single(i => i.Status == ImportStatus.VersionMismatch);
        mismatch.PackageId.Should().Be(ModId.From("author.mod"));
        mismatch.ListedVersion.Should().Be("1.0");
        mismatch.InstalledVersion.Should().Be("2.0");
    }
}
