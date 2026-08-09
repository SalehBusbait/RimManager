using FluentAssertions;
using RimManager.Core.Analysis;
using RimManager.Core.Locators;
using RimManager.Core.Parsing;
using RimManager.Core.Scanning;
using RimManager.Storage;
using RimManager.Storage.Analysis;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Runs the Cecil Harmony analyzer against the real install — the project's
/// differentiator. Skips cleanly where no install / no assemblies are present.
/// </summary>
public sealed class HarmonyAnalyzerLiveTests
{
    [SkippableFact]
    public void Finds_harmony_collisions_in_the_live_active_list()
    {
        var fs = new PhysicalFileSystem();
        var env = new PlatformEnvironment();

        var install = InstallLocator.LocateAll(env, fs).FirstOrDefault();
        Skip.If(install is null, "No RimWorld install detected.");
        var configDir = InstallLocator.LocateConfigDirectory(env, fs);
        Skip.If(configDir is null, "No config dir detected.");

        var config = ModsConfigParser.Parse(File.ReadAllText(Path.Combine(configDir!, "ModsConfig.xml")));
        var scan = new ModScanner(fs).Scan(install!.ToSourceRoots(), config.MajorMinor);
        var active = config.ActiveMods
            .Where(id => scan.ById.ContainsKey(id))
            .Select(id => scan.ById[id])
            .ToList();
        Skip.If(active.Count(m => m.HasAssemblies) < 5, "Not enough assembly-shipping mods to be meaningful.");

        var managed = Path.Combine(install.GameDir, "RimWorldWin64_Data", "Managed");
        var conflicts = HarmonyAnalyzer.Analyze(active, fs, config.MajorMinor,
            Directory.Exists(managed) ? managed : null);

        conflicts.Should().NotBeEmpty("a real 200-mod list has many mods patching the same vanilla methods");
        conflicts.Should().OnlyContain(c => c.Kind == ConflictKind.HarmonyPatch);
        conflicts.Should().OnlyContain(c => c.Mods.Length >= 2);
    }
}
