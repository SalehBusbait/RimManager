using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// A failure the user is told about must also be a failure a developer can read back.
/// <para>
/// The hub's load catch reported to the status bar and logged nothing, so the app's most
/// important failure left no trace in the Activity tab or the on-disk file — and "Copy
/// diagnostics bundle", the documented way to report a problem, handed over a log that
/// never mentioned it. Every sibling catch already paired its status line with a log
/// call; this pins that they all keep doing so.
/// </para>
/// <para>
/// Source checks, because <c>MainWindowViewModel</c> cannot be constructed under test and
/// the rule is about a call site rather than a value — the same shape as the schedule and
/// rule-source guards.
/// </para>
/// </summary>
public sealed class HubFailureReportingTests
{
    /// <summary>
    /// Catch blocks in the hub, as text. Bodies are taken up to the closing brace at the
    /// catch's own indentation, which is enough to see whether the block both reports and
    /// logs without pretending to parse C#.
    /// </summary>
    private static IEnumerable<(string File, int Line, string Body)> CatchBlocks()
    {
        foreach (var (path, text) in HubFiles())
        {
            foreach (Match m in Regex.Matches(text, @"^(?<indent>[ ]+)catch\b[^\n]*\n", RegexOptions.Multiline))
            {
                var closer = "\n" + m.Groups["indent"].Value + "}";
                var start = m.Index + m.Length;
                var end = text.IndexOf(closer, start, System.StringComparison.Ordinal);
                if (end < 0) continue;

                yield return (
                    System.IO.Path.GetFileName(path),
                    text.Take(m.Index).Count(c => c == '\n') + 1,
                    text[start..end]);
            }
        }
    }

    private static IEnumerable<(string Path, string Text)> HubFiles() =>
        System.IO.Directory
            .EnumerateFiles(
                System.IO.Path.Combine(RepoPaths.AppProject, "ViewModels"),
                "MainWindowViewModel*.cs")
            .Select(p => (p, System.IO.File.ReadAllText(p)));

    [Fact]
    public void Every_catch_that_tells_the_user_also_tells_the_log()
    {
        var offenders = CatchBlocks()
            .Where(c => c.Body.Contains("StatusText", System.StringComparison.Ordinal))
            .Where(c => !c.Body.Contains("_log.", System.StringComparison.Ordinal))
            .Select(c => $"{c.File}:{c.Line}")
            .ToList();

        offenders.Should().BeEmpty(
            "a failure reported only to the 24px status bar is gone by the next action and "
            + "absent from the diagnostics bundle — pair the status line with _log.Warn/Error");
    }

    /// <summary>
    /// The one write into the user's GAME folder is guarded.
    /// <para>
    /// <c>ApplyService</c> returns a non-written result for the running-game case alone;
    /// a read-only file, a denied ACL, a cloud-sync lock or a full disk all throw out of
    /// <c>AtomicWriteAsync</c>. Apply had no catch at all, and neither of its two callers
    /// could observe the exception — the commit bar awaits an <c>AsyncRelayCommand</c>
    /// (no unhandled-exception handler exists in this app) and <c>RequestApply</c>'s fast
    /// path discards the task outright.
    /// </para>
    /// </summary>
    [Fact]
    public void Applying_to_the_game_folder_is_wrapped()
    {
        var apply = HubFiles()
            .Select(f => f.Text)
            .Select(t => Regex.Match(t, @"private async Task Apply\(\).*?\n    \}", RegexOptions.Singleline))
            .FirstOrDefault(m => m.Success);

        apply.Should().NotBeNull("the Apply command must still exist to be guarded");

        apply!.Value.Should().Contain("_workspace.ApplyAsync",
            "this is the method that writes ModsConfig.xml");
        apply.Value.Should().Contain("catch",
            "the one write into the game folder must report its own failure rather than "
            + "surfacing as an unobserved task or an unhandled UI-thread exception");
        apply.Value.Should().Contain("finally",
            "_launchAfterWrite is reset in a finally: a throw used to skip the reset and "
            + "leave the flag armed, so the next ordinary apply launched the game by itself");
    }
}
