using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

public sealed class UpdateCheckerTests
{
    private static Mod Workshop(string packageId, string? publishedFileId) => new()
    {
        PackageId = ModId.From(packageId),
        Name = packageId,
        Source = publishedFileId is null ? ModSource.Local : ModSource.Workshop,
        RootPath = $"/mods/{packageId}",
        PublishedFileId = publishedFileId,
    };

    private static WorkshopInstallState Installed(params (string Id, long Time)[] items) => new()
    {
        Items = items.ToImmutableDictionary(
            i => i.Id,
            i => new InstalledWorkshopItem { PublishedFileId = i.Id, TimeUpdatedUtc = DateTimeOffset.FromUnixTimeSeconds(i.Time) },
            StringComparer.Ordinal),
    };

    private static WorkshopItem RemoteOk(string id, long time) => new()
    {
        PublishedFileId = id,
        Result = WorkshopItemResult.Ok,
        TimeUpdatedUtc = DateTimeOffset.FromUnixTimeSeconds(time),
    };

    private static Dictionary<string, WorkshopItem> Remote(params WorkshopItem[] items) =>
        items.ToDictionary(i => i.PublishedFileId, StringComparer.Ordinal);

    [Fact]
    public void Remote_newer_than_installed_is_update_available()
    {
        var status = UpdateChecker.Check(
            [Workshop("a.mod", "100")],
            Installed(("100", 1000)),
            Remote(RemoteOk("100", 2000))).Single();

        status.Status.Should().Be(UpdateStatus.UpdateAvailable);
        status.HasUpdate.Should().BeTrue();
        status.InstalledUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1000));
        status.RemoteUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(2000));
    }

    [Fact]
    public void Equal_or_older_remote_is_up_to_date()
    {
        var equal = UpdateChecker.Check([Workshop("a", "1")], Installed(("1", 1000)), Remote(RemoteOk("1", 1000))).Single();
        equal.Status.Should().Be(UpdateStatus.UpToDate);

        // Local newer than the live value (e.g. clock skew / re-publish reverted) — still not an update.
        var older = UpdateChecker.Check([Workshop("b", "2")], Installed(("2", 2000)), Remote(RemoteOk("2", 1000))).Single();
        older.Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Fact]
    public void Remote_not_found_is_delisted()
    {
        var notFound = new WorkshopItem { PublishedFileId = "9", Result = WorkshopItemResult.NotFound };
        var status = UpdateChecker.Check([Workshop("gone", "9")], Installed(("9", 1000)), Remote(notFound)).Single();

        status.Status.Should().Be(UpdateStatus.Delisted);
        status.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public void Mod_without_published_file_id_is_not_tracked()
    {
        var status = UpdateChecker.Check(
            [Workshop("local.only", null)], WorkshopInstallState.Empty, Remote()).Single();

        status.Status.Should().Be(UpdateStatus.NotTracked);
    }

    [Fact]
    public void Missing_from_manifest_is_not_tracked_even_with_remote()
    {
        // Remote known, but no installed timestamp to compare against → don't guess.
        var status = UpdateChecker.Check(
            [Workshop("a", "100")], WorkshopInstallState.Empty, Remote(RemoteOk("100", 2000))).Single();

        status.Status.Should().Be(UpdateStatus.NotTracked);
        status.RemoteUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(2000));
        status.InstalledUtc.Should().BeNull();
    }

    [Fact]
    public void Remote_absent_from_dict_is_not_tracked()
    {
        var status = UpdateChecker.Check(
            [Workshop("a", "100")], Installed(("100", 1000)), Remote()).Single();

        status.Status.Should().Be(UpdateStatus.NotTracked);
    }

    [Fact]
    public void Preserves_input_order_and_count()
    {
        var mods = new[] { Workshop("a", "1"), Workshop("b", "2"), Workshop("c", null) };
        var results = UpdateChecker.Check(mods, Installed(("1", 1000), ("2", 1000)),
            Remote(RemoteOk("1", 2000), RemoteOk("2", 1000)));

        results.Select(r => r.Id.Value).Should().Equal("a", "b", "c");
        results[0].Status.Should().Be(UpdateStatus.UpdateAvailable);
        results[1].Status.Should().Be(UpdateStatus.UpToDate);
        results[2].Status.Should().Be(UpdateStatus.NotTracked);
    }
}
