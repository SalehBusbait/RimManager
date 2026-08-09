using System.Collections.Generic;
using System.IO;
using System.Threading;
using RimManager.Core.Abstractions;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;
using RimManager.Storage.Analysis;

namespace RimManager.App.Services;

/// <summary>
/// The App's bridge to Tier-2 conflict analysis (spec §4.5): runs the Def / texture /
/// XML-patch / Harmony collision detectors over the active list. Thin I/O orchestration
/// over the already-tested analyzers — the same set the CLI <c>conflicts</c> command runs.
/// </summary>
public sealed class ConflictAnalysisService(IFileSystem fs)
{
    private readonly IFileSystem _fs = fs;

    /// <summary>Runs every collision analyzer over <paramref name="orderedActive"/> (load order matters:
    /// the last mod wins an override) and returns the merged report.</summary>
    /// <param name="progress">
    /// Ticked once per mod per pass, so the load state shows a real fraction.
    /// <para>
    /// The total is <c>mods × 4</c> and the count runs straight through all four passes,
    /// rather than resetting per analyzer. A bar that fills and restarts four times reads as
    /// four failures. It is not evenly paced — Harmony is by far the slowest of the four, so
    /// the last quarter is the slow one — but it only ever moves forwards, and the label says
    /// which pass it is in, which is the honest version of a number that cannot be linear.
    /// </para>
    /// </summary>
    public ConflictReport Analyze(
        IReadOnlyList<Mod> orderedActive, string? version, string? gameDir,
        IProgress<ScanProgress>? progress = null)
    {
        if (orderedActive.Count == 0) return ConflictReport.Empty;

        var total = orderedActive.Count * 4;
        var done = 0;

        Action<Mod> Tick(string pass) => _ =>
            progress?.Report(new ScanProgress(Interlocked.Increment(ref done), total, pass));

        var conflicts = new List<ModConflict>();
        conflicts.AddRange(DefCollisionAnalyzer.Analyze(orderedActive, _fs, version, Tick("def overrides")));
        conflicts.AddRange(PatchCollisionAnalyzer.Analyze(orderedActive, _fs, version, Tick("XML patches")));
        conflicts.AddRange(TextureCollisionAnalyzer.Analyze(orderedActive, _fs, version, Tick("textures")));
        conflicts.AddRange(HarmonyAnalyzer.Analyze(
            orderedActive, _fs, version, FindManagedDir(gameDir), Tick("Harmony patches")));

        return new ConflictReport([.. conflicts]);
    }

    /// <summary>Locates the game's <c>Managed</c> dir so Harmony targets resolve; null if not found
    /// (Harmony analysis still runs, just with poorer resolution).</summary>
    private string? FindManagedDir(string? gameDir)
    {
        if (gameDir is null) return null;

        string[] candidates =
        [
            Path.Combine("RimWorldWin64_Data", "Managed"),
            Path.Combine("RimWorldLinux_Data", "Managed"),
            Path.Combine("RimWorldMac.app", "Contents", "Resources", "Data", "Managed"),
            Path.Combine("Data", "Managed"),
        ];

        foreach (var rel in candidates)
        {
            var dir = Path.Combine(gameDir, rel);
            if (_fs.FileExists(Path.Combine(dir, "Assembly-CSharp.dll"))) return dir;
        }

        return null;
    }
}
