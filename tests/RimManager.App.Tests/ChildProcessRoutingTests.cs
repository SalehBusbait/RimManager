using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The app's exe doubles as its own Steamworks helper: <c>Program.Main</c> checks
/// <c>args[0]</c> against a marker and routes to a child mode <b>before any UI
/// exists</b>. Nothing about that arrangement fails loudly when it is wrong.
/// <para>
/// A marker declared but never routed compiles, passes every other test, and at
/// runtime falls through to <c>BuildAvaloniaApp()</c> — so the "helper" the parent
/// spawned is a second copy of the GUI, which then exits only when the 90s child
/// timeout kills it. The user sees an update that silently did nothing. This is the
/// same silent-failure shape the markup guards exist for, one layer down.
/// </para>
/// </summary>
public sealed class ChildProcessRoutingTests
{
    private static string ProgramSource =>
        File.ReadAllText(Path.Combine(RepoPaths.AppProject, "Program.cs"));

    private static string SteamworksDir =>
        Path.Combine(RepoPaths.Root, "src", "RimManager.Integrations", "Steamworks");

    /// <summary>
    /// Every <c>ArgumentMarker</c> under Integrations/Steamworks must be routed by
    /// name in Program.Main. Matching on the CONSTANT rather than the string is
    /// deliberate: it is the declaration that a child mode exists, and re-typing the
    /// literal in Program.cs would satisfy a text search while still drifting.
    /// </summary>
    [Fact]
    public void Every_steamworks_child_mode_is_routed_in_Main()
    {
        var program = ProgramSource;
        var declaring = Directory.EnumerateFiles(SteamworksDir, "*.cs")
            .Where(f => File.ReadAllText(f).Contains("public const string ArgumentMarker"))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        declaring.Should().NotBeEmpty("the helper children are what this guards");

        foreach (var type in declaring)
        {
            program.Should().Contain($"{type}.ArgumentMarker",
                $"{type} declares a child-process marker, and a marker Main does not "
                + "route falls through to BuildAvaloniaApp — the parent then waits 90s "
                + "on a second copy of the GUI while the user is told an update ran");
            program.Should().Contain($"{type}.Run(",
                $"{type}'s marker must reach its own entry point");
        }
    }

    /// <summary>
    /// Routing must precede UI construction. Avalonia's startup is not free and the
    /// child has no display; more to the point, a child that builds a window is a
    /// child that does not promptly exit, and its exit is the entire mechanism —
    /// only the process ending lets Steam stop treating RimWorld as running and
    /// start the downloads.
    /// </summary>
    [Fact]
    public void Routing_happens_before_any_UI_is_built()
    {
        var program = ProgramSource;
        var firstRoute = program.IndexOf(".ArgumentMarker", System.StringComparison.Ordinal);
        var buildApp = program.IndexOf("BuildAvaloniaApp()", System.StringComparison.Ordinal);

        firstRoute.Should().BeGreaterThan(-1);
        buildApp.Should().BeGreaterThan(-1);
        firstRoute.Should().BeLessThan(buildApp,
            "the child must be routed before Avalonia is touched");
    }

    /// <summary>
    /// The rule that cost this project two commits and one false finding: asking the
    /// Steam client for a UGC interface BY VERSION STRING hands back a real object of
    /// the wrong version, and the game's flat functions — compiled against exactly one
    /// vtable layout — then index shifted slots. The symptom is not a crash but a call
    /// that returns a plausible value and does nothing, which is precisely what made a
    /// working <c>DownloadItem</c> look inert.
    /// </summary>
    /// <remarks>
    /// Matched as a QUOTED literal, because that is the only form that can do harm:
    /// an export is reached by name through <c>GetExport</c>/<c>TryGetExport</c>, so
    /// a violation must spell it in quotes. The bare name appears in these files on
    /// purpose — the comments explaining why it is banned are the most valuable text
    /// in the folder, and a guard that forbade discussing the rule would get the rule
    /// deleted.
    /// </remarks>
    [Fact]
    public void The_UGC_interface_is_never_acquired_by_version_string()
    {
        foreach (var file in Directory.EnumerateFiles(SteamworksDir, "*.cs"))
        {
            File.ReadAllText(file).Should().NotContain("\"SteamInternal_FindOrCreateUserInterface\"",
                $"{Path.GetFileName(file)} must take the interface from the dll's own "
                + "versioned accessor only — asking the client by string is what "
                + "dispatched a v016 DownloadItem into a v022 vtable");
        }
    }

    /// <summary>
    /// <c>SteamAPI_RunCallbacks</c> needs the SDK's C++ callback-manager state to
    /// dispatch into, which a flat P/Invoke binding does not have. Pumping it with a
    /// pending call result corrupted the session and killed the next native call.
    /// </summary>
    [Fact]
    public void RunCallbacks_is_never_pumped_from_a_flat_binding()
    {
        foreach (var file in Directory.EnumerateFiles(SteamworksDir, "*.cs"))
        {
            File.ReadAllText(file).Should().NotContain("\"SteamAPI_RunCallbacks\"",
                $"{Path.GetFileName(file)} must not pump callbacks — the queued "
                + "operations execute client-side without any dispatch");
        }
    }

    /// <summary>
    /// The updater must not reacquire the resubscribe. It was adopted on a finding
    /// measured through two defects since fixed, retested on 9 Aug 2026 and removed:
    /// a bare DownloadItem is sufficient, and it carries no window in which the user
    /// is unsubscribed from their own mods. Re-adding <c>UnsubscribeItem</c> would
    /// reintroduce that hazard, so it should be an argued change and not a quiet one.
    /// </summary>
    [Fact]
    public void The_updater_does_not_unsubscribe_the_user()
    {
        var updater = Path.Combine(SteamworksDir, "SteamworksDownload.cs");

        File.Exists(updater).Should().BeTrue();
        File.ReadAllText(updater).Should().NotContain("UnsubscribeItem",
            "updating must never pass through a state where the user is unsubscribed");
    }
}
