using System.Diagnostics;
using RimManager.Core.Abstractions;

namespace RimManager.Storage;

/// <summary>
/// Real <see cref="IGameProcessDetector"/>: scans running processes for RimWorld.
/// Matches any process whose name contains "rimworld" (case-insensitive), which
/// covers <c>RimWorldWin64</c>, <c>RimWorldLinux</c>, the macOS <c>RimWorld</c>
/// bundle, and the Proton case where the Windows exe runs under a Linux Steam.
/// </summary>
public sealed class RimWorldProcessDetector : IGameProcessDetector
{
    public bool IsGameRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.Contains("rimworld", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited between enumeration and inspection — ignore.
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Access denied to a system process, or name unavailable — ignore.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }
}
