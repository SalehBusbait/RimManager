using System.Diagnostics;

namespace RimManager.Storage;

/// <summary>
/// Whether the Steam client is running, for the Steam card on Settings ▸ Integrations.
/// <para>
/// A local process probe and nothing more. RimManager holds no Steam session: what it
/// actually does with a running client is hand it a <c>steam://</c> URL — open a mod's
/// Workshop page, or a collection's "Subscribe to all". So the card reports
/// <b>running</b> rather than <b>connected</b>; there is no connection to report on.
/// </para>
/// <para>
/// Sits beside <see cref="RimWorldProcessDetector"/> because process enumeration is
/// I/O and <c>Core</c> performs none.
/// </para>
/// </summary>
public sealed class SteamClientDetector
{
    /// <summary>
    /// Exact process names, one per platform: <c>steam</c> on Windows (Steam.exe) and
    /// Linux, <c>steam_osx</c> on macOS. <c>steamwebhelper</c> is included because the
    /// Linux launcher is a shell script that can exit while the client stays up.
    /// <para>
    /// Matched exactly rather than by substring — <c>steamerrorreporter</c> and
    /// <c>steamservice</c> both contain "steam" and neither means the client is up.
    /// </para>
    /// </summary>
    private static readonly string[] ClientProcessNames =
        ["steam", "steam_osx", "steamwebhelper"];

    public bool IsClientRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                foreach (var name in ClientProcessNames)
                {
                    if (process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Exited between enumeration and inspection — ignore.
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Access denied to a system process, or the name is unavailable — ignore.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }
}
