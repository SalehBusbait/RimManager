using System.Diagnostics;
using RimManager.Core.Abstractions;

namespace RimManager.Integrations.Processes;

/// <summary>
/// The one place RimManager launches an external process for git.
/// <para>
/// Lives in <c>Integrations</c> alongside the other real-world edges (HTTP, SteamCMD)
/// so <c>Core</c> keeps its no-I/O rule. Arguments are passed through
/// <see cref="ProcessStartInfo.ArgumentList"/> rather than a joined string, so a mod
/// folder with a space or a quote in its name cannot turn into extra arguments.
/// </para>
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private readonly TimeSpan _timeout;

    /// <param name="timeout">
    /// A hung git — waiting on a credential prompt it can never receive, most often —
    /// must not hang a mod scan. Defaults to 30 seconds.
    /// </param>
    public SystemProcessRunner(TimeSpan? timeout = null) =>
        _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        // Never let git stop for input it cannot get: with no terminal attached a
        // credential prompt would block until the timeout instead of failing fast.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start()) return NotRun($"could not start '{fileName}'");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // git not installed, or not on PATH. A normal state, not an error:
            // every integration is optional and the app works offline.
            return NotRun(ex.Message);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(_timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return new ProcessResult(
                process.ExitCode,
                (await stdout.ConfigureAwait(false)).TrimEnd(),
                (await stderr.ConfigureAwait(false)).TrimEnd());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return NotRun(ct.IsCancellationRequested ? "cancelled" : $"timed out after {_timeout.TotalSeconds:0}s");
        }
    }

    /// <summary>Exit code -1 marks "never produced a result", distinct from a git failure.</summary>
    private static ProcessResult NotRun(string reason) => new(-1, string.Empty, reason);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone, or not ours to kill.
        }
    }
}
