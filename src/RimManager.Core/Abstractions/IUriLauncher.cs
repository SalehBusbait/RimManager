namespace RimManager.Core.Abstractions;

/// <summary>
/// Opens a URI with the OS's registered handler (a <c>steam://</c> deep-link, an
/// <c>https://</c> page). The seam that keeps launching out of <c>Core</c>: the domain
/// decides <em>what</em> URL to open (via <c>SteamUrls</c>), a concrete launcher in
/// <c>RimManager.Storage</c> decides <em>how</em> (ShellExecute / <c>open</c> /
/// <c>xdg-open</c>), and tests use a recording double.
/// </summary>
public interface IUriLauncher
{
    /// <summary>
    /// Hands <paramref name="uri"/> to the OS handler. Throws if the launch fails
    /// (no handler registered, process start denied) so callers can offer a fallback.
    /// </summary>
    void Launch(string uri);
}
