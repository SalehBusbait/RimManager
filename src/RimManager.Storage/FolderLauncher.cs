using System.Diagnostics;

namespace RimManager.Storage;

/// <summary>
/// Opens a folder in the platform's file manager.
/// <para>
/// Separate from <see cref="ShellUriLauncher"/> deliberately. That class refuses anything
/// outside its <c>steam://</c> / <c>http(s)://</c> allowlist precisely so a bare path can
/// never be handed to the shell as a program — which meant every "open folder" action in
/// the app was calling it with a directory, having the <c>ArgumentException</c> swallowed
/// by a <c>catch</c>, and doing nothing at all. Loosening the allowlist to fix that would
/// have removed the protection it exists for.
/// </para>
/// </summary>
public sealed class FolderLauncher
{
    /// <summary>
    /// Reveals <paramref name="path"/> in the file manager.
    /// </summary>
    /// <returns>Null on success, or a message describing why not.</returns>
    public string? Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "No folder to open.";

        // Must be a DIRECTORY that exists. Checked rather than assumed: this hands a
        // string to the shell, and the whole point of keeping it out of the URI launcher
        // is that "open this" and "run this" must not be the same code path.
        if (!Directory.Exists(path)) return $"'{path}' does not exist.";

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", [path]))?.Dispose();
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", [path])?.Dispose();
            }
            else
            {
                Process.Start("xdg-open", [path])?.Dispose();
            }

            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                       or InvalidOperationException
                                       or FileNotFoundException)
        {
            // A headless Linux box has no xdg-open. Normal, and worth saying rather than
            // failing silently the way the previous route did.
            return ex.Message;
        }
    }
}
