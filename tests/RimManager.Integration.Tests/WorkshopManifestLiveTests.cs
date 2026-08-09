using FluentAssertions;
using RimManager.Core.Locators;
using RimManager.Core.Workshop;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Parses the real <c>appworkshop_294100.acf</c> from this machine's Steam library,
/// if present. Skips (never fails) on a runner without a RimWorld install.
/// </summary>
public sealed class WorkshopManifestLiveTests
{
    private const int RimWorldAppId = 294100;

    [SkippableFact]
    public void Parses_the_real_workshop_manifest_with_install_times()
    {
        var fs = new PhysicalFileSystem();
        var env = new PlatformEnvironment();

        var install = InstallLocator.LocateAll(env, fs).FirstOrDefault();
        Skip.If(install?.WorkshopDir is null, "No RimWorld Workshop dir on this machine.");

        // …/workshop/content/294100 → …/workshop/appworkshop_294100.acf
        var workshopRoot = Path.GetDirectoryName(Path.GetDirectoryName(install!.WorkshopDir!.TrimEnd('/', '\\')));
        Skip.If(workshopRoot is null, "Unexpected Workshop dir layout.");
        var acfPath = Path.Combine(workshopRoot!, $"appworkshop_{RimWorldAppId}.acf");
        Skip.IfNot(fs.FileExists(acfPath), "Steam workshop manifest not present.");

        var state = WorkshopManifestParser.Parse(fs.ReadAllText(acfPath));

        state.Items.Should().NotBeEmpty("a used install has subscribed items recorded");
        state.Items.Values.Should().Contain(i => i.TimeUpdatedUtc != null,
            "at least some installed items carry a Steam publish time");
    }
}
