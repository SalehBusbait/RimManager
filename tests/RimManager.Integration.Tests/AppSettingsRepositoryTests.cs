using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using RimManager.Storage;
using RimManager.Storage.Repositories;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// The app preference file, on real disk. Every page of Settings said "these take effect
/// immediately" and then forgot them on the next launch; this is what makes that true
/// across restarts.
/// </summary>
public sealed class AppSettingsRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rimmanager-settings-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly PhysicalFileSystem _fs = new(SystemClock.Instance);

    private AppSettingsRepository Repo() =>
        new(_fs, Path.Combine(_dir, "settings.json"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void No_file_yet_gives_the_designed_defaults()
    {
        var settings = Repo().Load();

        settings.Theme.Should().Be("FollowSystem");
        settings.ShowTagStripes.Should().BeTrue();
        settings.ZebraStriping.Should().BeFalse();
        settings.UseTopologicalSort.Should().BeTrue();
        settings.AutoSortAfterActivate.Should().BeFalse("design non-negotiable #8");
    }

    [Fact]
    public async Task Every_preference_survives_the_round_trip()
    {
        var written = new AppSettings
        {
            Theme = "Ice",
            FontIndex = 2,
            IsComfortableDensity = true,
            ShowTagStripes = false,
            ZebraStriping = true,
            ShowPreviewImages = false,
            UseTopologicalSort = false,
            SnapshotBeforeSorting = false,
            OpenDockOnCycleBreak = false,
            AutoSortAfterActivate = true,
            ShowGitDirtyOnRows = false,
            FetchReposOnStartup = true,
            AutoInstallUpdates = true,
            // N7c/N7d: the database toggles and custom source URLs.
            UseCommunityRules = false,
            UseReplacementsDatabase = false,
            UseKnownGoodDatabase = false,
            CommunityRulesUrl = "https://example.test/rules.json",
            ReplacementsUrl = "https://example.test/replacements.json.gz",
            KnownGoodBaseUrl = "https://example.test/knowngood",
        };

        await Repo().SaveAsync(written);

        // A fresh repository, as a restart would build.
        Repo().Load().Should().BeEquivalentTo(written,
            "a preference that is not round-tripped is one the user re-sets on every launch");
    }

    /// <summary>
    /// Theme is stored by NAME. An ordinal would be silently reinterpreted the day a
    /// fourth theme is inserted, turning everyone's "Dark" into something else.
    /// </summary>
    [Fact]
    public async Task The_theme_is_written_as_a_name_not_a_number()
    {
        await Repo().SaveAsync(new AppSettings { Theme = "Dark" });

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));

        json.Should().Contain("\"Dark\"");
        json.Should().NotContain("\"theme\": 2").And.NotContain("\"Theme\": 2");
    }

    /// <summary>
    /// A corrupt preference file must not stop the app starting. Losing a theme choice is
    /// a nuisance; refusing to launch over one is not a trade worth making.
    /// </summary>
    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_rather_than_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not json");

        var settings = Repo().Load();

        settings.Should().BeEquivalentTo(new AppSettings());
    }

    /// <summary>
    /// Written without a timestamped backup. It changes on every toggle flip, and a backup
    /// per flip would bury the profile and tag backups constraint #5 exists to protect.
    /// </summary>
    [Fact]
    public async Task Saving_repeatedly_does_not_litter_backups()
    {
        var repo = Repo();
        for (var i = 0; i < 5; i++) await repo.SaveAsync(new AppSettings { FontIndex = i });

        Directory.GetFiles(_dir).Should().ContainSingle()
            .Which.Should().EndWith("settings.json");
    }
}
