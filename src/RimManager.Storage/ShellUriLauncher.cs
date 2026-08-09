using System.Diagnostics;
using RimManager.Core.Abstractions;

namespace RimManager.Storage;

/// <summary>
/// Opens a URI with the platform's default handler. The one place the app shells out
/// to open a link, so the scheme allowlist lives here: only <c>steam://</c> and
/// <c>http(s)://</c> are ever launched — never <c>file:</c>, <c>javascript:</c>, or a
/// bare path that could be interpreted as an executable.
/// </summary>
public sealed class ShellUriLauncher : IUriLauncher
{
    public void Launch(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (!IsAllowedScheme(uri))
        {
            throw new ArgumentException($"Refusing to launch a non-allowlisted URI scheme: '{uri}'.", nameof(uri));
        }

        // UseShellExecute routes through the OS handler (protocol registration).
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", [uri])?.Dispose();
        }
        else
        {
            Process.Start("xdg-open", [uri])?.Dispose();
        }
    }

    private static bool IsAllowedScheme(string uri) =>
        uri.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
