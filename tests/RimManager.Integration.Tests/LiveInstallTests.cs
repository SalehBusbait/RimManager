using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Locators;
using RimManager.Core.Scanning;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// End-to-end against a real RimWorld install on this machine. Skips (never fails)
/// where no install is present, so CI on a bare runner stays green.
/// </summary>
public sealed class LiveInstallTests
{
    [SkippableFact]
    public void Detects_install_and_resolves_harmony_assemblies_via_loadfolders()
    {
        var fs = new PhysicalFileSystem();
        var env = new PlatformEnvironment();

        var install = InstallLocator.LocateAll(env, fs).FirstOrDefault();
        Skip.If(install is null, "No RimWorld install detected on this machine.");
        Skip.If(install!.WorkshopDir is null, "Install has no Workshop dir.");

        var result = new ModScanner(fs).Scan(install.ToSourceRoots(), "1.6");

        result.Mods.Length.Should().BeGreaterThan(10);

        var harmony = result.ById.GetValueOrDefault(ModId.From("brrainz.harmony"));
        Skip.If(harmony is null, "Harmony not subscribed on this machine.");

        // The whole point of the LoadFolders fix: Harmony's dll lives under Current/,
        // so a naive version-subfolder scan would miss it.
        harmony!.HasAssemblies.Should().BeTrue("Harmony ships assemblies under a LoadFolders-mapped folder");
    }
}
