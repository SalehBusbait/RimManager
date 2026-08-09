using System.Diagnostics;

namespace RimManager.Storage;

/// <summary>
/// Starts the game and does not wait for it. The one place RimManager launches a
/// long-lived, detached process.
/// <para>
/// Separate from <see cref="ShellUriLauncher"/> on purpose: that one is scheme-allowlisted
/// precisely so a bare path can never be run as a program, which is exactly what this
/// does. Keeping them apart means the allowlist cannot be loosened by accident to make
/// launching work.
/// </para>
/// </summary>
public sealed class GameProcessLauncher
{
    /// <summary>
    /// Starts <paramref name="fileName"/> with <paramref name="arguments"/>.
    /// </summary>
    /// <param name="workingDirectory">
    /// The game folder. RimWorld resolves some of its own relative paths against the
    /// working directory, so launching from RimManager's own is not equivalent.
    /// </param>
    /// <returns>Null on success, or a message describing why it did not start.</returns>
    public string? Launch(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? string.Empty,

            // Shell execution: it is what resolves a bare "steam" against PATH and lets
            // the OS handle a .app bundle on macOS. It also means we get no stdout, which
            // is correct — the game owns its console, and we are not supervising it.
            UseShellExecute = true,
        };

        // ArgumentList rather than a joined string, so a folder with a space in its name
        // stays one argument instead of becoming two.
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(psi);
            return process is null ? $"'{fileName}' did not start." : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                       or InvalidOperationException
                                       or System.IO.FileNotFoundException)
        {
            // Most often: the command names a program that is not installed or not on
            // PATH. A launch command is user-editable, so this is a normal wrong answer
            // to report, not a crash.
            return ex.Message;
        }
    }
}
