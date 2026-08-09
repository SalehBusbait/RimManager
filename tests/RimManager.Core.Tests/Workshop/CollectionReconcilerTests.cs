using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class CollectionReconcilerTests
{
    private static Mod InstalledWorkshopMod(string packageId, string publishedFileId) => new()
    {
        PackageId = ModId.From(packageId),
        Name = packageId,
        Source = ModSource.Workshop,
        RootPath = $"/ws/{publishedFileId}",
        PublishedFileId = publishedFileId,
    };

    private static WorkshopItem MetaOk(string id, string title) => new()
    {
        PublishedFileId = id,
        Result = WorkshopItemResult.Ok,
        Title = title,
    };

    [Fact]
    public void Classifies_installed_missing_and_delisted_members()
    {
        var installed = CollectionReconciler.IndexByFileId([InstalledWorkshopMod("author.have", "100")]);
        var metadata = new Dictionary<string, WorkshopItem>
        {
            ["100"] = MetaOk("100", "Have It"),
            ["200"] = MetaOk("200", "Need It"),
            ["300"] = new() { PublishedFileId = "300", Result = WorkshopItemResult.NotFound },
        };

        var report = CollectionReconciler.Reconcile(["100", "200", "300"], installed, metadata);

        report.Members.Should().HaveCount(3);
        report.InstalledCount.Should().Be(1);
        report.MissingCount.Should().Be(2);

        var have = report.Members[0];
        have.IsInstalled.Should().BeTrue();
        have.InstalledPackageId.Should().Be(ModId.From("author.have"));
        have.DisplayName.Should().Be("Have It");

        var need = report.Members[1];
        need.IsInstalled.Should().BeFalse();
        need.IsDelisted.Should().BeFalse();
        need.DisplayName.Should().Be("Need It");

        var gone = report.Members[2];
        gone.IsInstalled.Should().BeFalse();
        gone.IsDelisted.Should().BeTrue();
    }

    [Fact]
    public void Preserves_member_order_and_skips_blanks()
    {
        var report = CollectionReconciler.Reconcile(
            ["3", "", "1", "  ", "2"],
            new Dictionary<string, Mod>(),
            metadataById: null);

        report.Members.Select(m => m.PublishedFileId).Should().Equal("3", "1", "2");
        report.MissingCount.Should().Be(3);
    }

    [Fact]
    public void Without_metadata_falls_back_to_id_for_missing_members()
    {
        var report = CollectionReconciler.Reconcile(["999"], new Dictionary<string, Mod>());

        var member = report.Members.Single();
        member.Title.Should().BeNull();
        member.DisplayName.Should().Be("999");
    }

    [Fact]
    public void Missing_enumerates_only_uninstalled_members()
    {
        var installed = CollectionReconciler.IndexByFileId([InstalledWorkshopMod("a", "1")]);
        var report = CollectionReconciler.Reconcile(["1", "2"], installed);

        report.Missing.Select(m => m.PublishedFileId).Should().Equal("2");
    }
}
