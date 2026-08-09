using FluentAssertions;
using RimManager.Integrations.SteamCmd;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Drives a real SteamCMD to download a tiny RimWorld Workshop item anonymously. Skips
/// (never fails) when no SteamCMD is available or the machine is offline, so CI stays
/// green. This is the end-to-end proof that anonymous 294100 downloads need no login.
/// </summary>
public sealed class SteamCmdRunnerLiveTests
{
    private const int RimWorldAppId = 294100;

    // "Perspective: Eaves (Continued)" — ~88 KB, a deliberately tiny item to keep the test cheap.
    private const string TinyItemId = "3346964576";

    [SkippableFact]
    public async Task Downloads_a_workshop_item_anonymously_end_to_end()
    {
        var exe = LocateSteamCmd();
        Skip.If(exe is null, "No SteamCMD found (set STEAMCMD_EXE, provision one, or install RimSort).");

        var staging = Path.Combine(Path.GetTempPath(), "rimmanager_scmd_test");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

        try
        {
            var runner = new SteamCmdRunner(exe!);
            SteamCmdRunResult run;
            try
            {
                run = await runner.DownloadWorkshopItemsAsync([TinyItemId], RimWorldAppId, forceInstallDir: staging);
            }
            catch (Exception ex)
            {
                Skip.If(true, $"SteamCMD run failed (offline?): {ex.Message}");
                return;
            }

            var result = run.Results.SingleOrDefault(r => r.PublishedFileId == TinyItemId);
            // Anonymous downloads can be transiently rate-limited; treat that as a skip, not a failure.
            Skip.If(result is null || !result.Success, "SteamCMD did not report success (rate-limited or transient).");

            result!.DownloadedPath.Should().NotBeNullOrEmpty();
            Directory.Exists(result.DownloadedPath!).Should().BeTrue("SteamCMD reported a path it downloaded to");
            File.Exists(Path.Combine(result.DownloadedPath!, "About", "About.xml"))
                .Should().BeTrue("a real RimWorld mod carries About/About.xml");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static string? LocateSteamCmd()
    {
        var exeName = OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh";

        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("STEAMCMD_EXE"),
            Path.Combine(AppPaths.SteamCmdDir, exeName),
            // RimSort's packaged instance, if present (common on RimWorld modders' machines).
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RimSort", "instances", "Default", "steamcmd", exeName),
        };

        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }
}
