namespace RimManager.Core.Abstractions;

/// <summary>The outcome of running an external command.</summary>
/// <param name="ExitCode">Process exit code; 0 is success by convention.</param>
/// <param name="StandardOutput">Captured stdout, trailing newline trimmed.</param>
/// <param name="StandardError">Captured stderr, trailing newline trimmed.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>stdout as lines, blank entries dropped — the shape most git output wants.</summary>
    public IReadOnlyList<string> OutputLines =>
        StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Runs an external command. The seam exists for the same reason
/// <see cref="IFileSystem"/> does: <c>Core</c> performs no I/O, and "did we build the
/// right git command line" is worth testing without a git binary, a network, or a
/// real repository on disk.
/// </summary>
public interface IProcessRunner
{
    /// <param name="workingDirectory">
    /// Where to run. For git this is the repository, and getting it wrong is the most
    /// common way these commands silently answer about the wrong tree.
    /// </param>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default);
}
