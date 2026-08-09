using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.ModDatabases;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.ModDatabases;

/// <summary>
/// The UseThisInstead client and matcher (N7). The odd-looking cases are measured
/// facts about the live database, not invented edge cases: a BOM inside the gzip,
/// 4 rules with null or numeric packageIds, 374 rules keeping one packageId across
/// old and new, 230 replacements that stop short of 1.6.
/// </summary>
public sealed class UseThisInsteadTests
{
    private const string SampleJson = """
        {
          "rules": [
            {
              "oldWorkshopId": "111", "oldName": "Therapy", "oldPackageId": "Orion.Therapy",
              "newWorkshopId": "222", "newName": "Therapy (Continued)", "newAuthor": "Zaljerem",
              "newPackageId": "Orion.Therapy",
              "oldVersions": ["1.4", "1.5"], "newVersions": ["1.6"]
            },
            {
              "oldWorkshopId": "333", "oldName": "Space Worms", "oldPackageId": "",
              "newWorkshopId": "444", "newName": "Space Worms (Continued)", "newAuthor": "Mlie",
              "newPackageId": "Mlie.SpaceWorms",
              "newVersions": ["1.5"]
            },
            {
              "oldWorkshopId": "555", "oldName": "Numeric Id", "oldPackageId": 1797397487,
              "newWorkshopId": "666", "newName": "Numeric (Continued)", "newAuthor": "Mlie",
              "newPackageId": "Mlie.Numeric",
              "newVersions": ["1.6"]
            }
          ],
          "version": "2026-08-06T19:05:04Z"
        }
        """;

    private static Mod Mod(string id, string? fileId = null) => new()
    {
        PackageId = ModId.From(id),
        Name = id,
        Source = fileId is null ? ModSource.Local : ModSource.Workshop,
        RootPath = "/" + id,
        PublishedFileId = fileId,
    };

    [Fact]
    public void Parses_the_rules_and_the_version_stamp()
    {
        var db = UseThisInsteadParser.Parse(SampleJson);

        db.Count.Should().Be(3);
        db.PublishedUtc.Should().Be(DateTimeOffset.Parse("2026-08-06T19:05:04Z"));

        var therapy = db.Replacements[0];
        therapy.OldWorkshopId.Should().Be("111");
        therapy.OldPackageId.Should().Be(ModId.From("Orion.Therapy"));
        therapy.NewName.Should().Be("Therapy (Continued)");
        therapy.NewVersions.Should().Equal("1.6");
    }

    [Fact]
    public void A_null_or_numeric_packageId_degrades_to_none_and_the_rule_survives()
    {
        var db = UseThisInsteadParser.Parse(SampleJson);

        db.Replacements[1].OldPackageId.Should().BeNull("empty string");
        db.Replacements[2].OldPackageId.Should().BeNull("a numeric value is not a packageId");
        db.Replacements[2].OldWorkshopId.Should().Be("555", "the rule itself is still usable by id");
    }

    [Fact]
    public void Garbage_and_wrong_shapes_yield_the_empty_database_never_a_throw()
    {
        UseThisInsteadParser.Parse("not json").Count.Should().Be(0);
        UseThisInsteadParser.Parse("[]").Count.Should().Be(0);
        UseThisInsteadParser.Parse("{}").Count.Should().Be(0);
    }

    [Fact]
    public async Task The_client_gunzips_and_strips_the_upstream_BOM()
    {
        // The live payload is gzip with a UTF-8 BOM inside — reproduce both.
        using var compressed = new MemoryStream();
        await using (var gz = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            await gz.WriteAsync(bom);
            await gz.WriteAsync(Encoding.UTF8.GetBytes(SampleJson));
        }

        var fetcher = new FakeHttpFetcher { BytesResponder = _ => compressed.ToArray() };
        var db = await new UseThisInsteadClient(fetcher).FetchAsync();

        db.Count.Should().Be(3);
        fetcher.GetCalls.Should().ContainSingle().Which.Should().Be(UseThisInsteadClient.DefaultUrl);
        db.RawJson.Should().NotStartWith("﻿", "the cache must not re-carry the BOM");
    }

    // --- the matcher ---------------------------------------------------------

    private static ImmutableArray<ModReplacement> Rules =>
        UseThisInsteadParser.Parse(SampleJson).Replacements;

    [Fact]
    public void A_workshop_mod_matches_by_file_id_alone()
    {
        ReplacementMatcher.For(Mod("orion.therapy", fileId: "111"), Rules, "1.6")
            .Should().NotBeNull().And.Match<ModReplacement>(r => r.NewWorkshopId == "222");
    }

    [Fact]
    public void The_replacement_itself_is_never_flagged_despite_sharing_the_packageId()
    {
        // The continued mod keeps Orion.Therapy but lives at file id 222 — 374 live
        // rules have this shape, and flagging it would tell the user to replace the
        // replacement.
        ReplacementMatcher.For(Mod("orion.therapy", fileId: "222"), Rules, "1.6")
            .Should().BeNull();
    }

    [Fact]
    public void A_local_copy_matches_by_packageId_only_when_old_and_new_differ()
    {
        // Same-id rule: a local Orion.Therapy could be either side — no accusation.
        ReplacementMatcher.For(Mod("orion.therapy"), Rules, "1.6").Should().BeNull();

        // Different-id rule with a local copy: the packageId fallback would apply,
        // but rule 333's oldPackageId is empty so nothing matches — build one where
        // it is present.
        var rules = ImmutableArray.Create(new ModReplacement(
            "777", ModId.From("old.mod"), "Old",
            "888", ModId.From("new.mod"), "New", "Mlie", ["1.6"]));

        ReplacementMatcher.For(Mod("old.mod"), rules, "1.6").Should().NotBeNull();
        ReplacementMatcher.For(Mod("new.mod"), rules, "1.6").Should().BeNull();
    }

    [Fact]
    public void A_replacement_that_does_not_support_the_running_version_is_not_offered()
    {
        // Rule 333's replacement stops at 1.5 — 230 live rules stop short of 1.6.
        ReplacementMatcher.For(Mod("x", fileId: "333"), Rules, "1.6").Should().BeNull();
        ReplacementMatcher.For(Mod("x", fileId: "333"), Rules, "1.5").Should().NotBeNull();

        // Version unknown gates nothing rather than guessing.
        ReplacementMatcher.For(Mod("x", fileId: "333"), Rules, null).Should().NotBeNull();
    }
}
