using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;

namespace RimManager.Integrations.SteamCmd;

/// <summary>
/// Ensures a RimManager-private SteamCMD instance exists in its own directory —
/// isolated from any system/RimSort install — by fetching Valve's official bootstrapper
/// on demand. We fetch rather than embed: SteamCMD self-updates on first run (so a bundled
/// copy is stale), that first run pulls a few hundred MB, and this avoids redistributing
/// Valve's binary ourselves.
/// </summary>
public sealed class SteamCmdProvisioner(string installDir)
{
    public string InstallDir { get; } = installDir;

    /// <summary>The SteamCMD entry point for this platform inside <see cref="InstallDir"/>.</summary>
    public string ExePath => Path.Combine(InstallDir, OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh");

    public bool IsProvisioned => File.Exists(ExePath);

    private static string BootstrapUrl => OperatingSystem.IsWindows()
        ? "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"
        : OperatingSystem.IsMacOS()
            ? "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz"
            : "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";

    /// <summary>
    /// Returns the path to a ready SteamCMD, provisioning it if absent. The first real
    /// download after provisioning triggers SteamCMD's own ~200–300 MB self-update — so
    /// callers should surface that this can be slow the first time.
    /// </summary>
    public async Task<string> EnsureProvisionedAsync(CancellationToken ct = default)
    {
        if (IsProvisioned) return ExePath;

        Directory.CreateDirectory(InstallDir);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RimManager/1.0");
        await using var archive = await http.GetStreamAsync(BootstrapUrl, ct).ConfigureAwait(false);

        if (OperatingSystem.IsWindows())
        {
            using var zip = new ZipArchive(await BufferAsync(archive, ct).ConfigureAwait(false), ZipArchiveMode.Read);
            zip.ExtractToDirectory(InstallDir, overwriteFiles: true);
        }
        else
        {
            await using var gz = new GZipStream(archive, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, InstallDir, overwriteFiles: true, ct).ConfigureAwait(false);
            // tar preserves the +x bit; guard in case it didn't.
            if (File.Exists(ExePath) && !OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(ExePath);
                File.SetUnixFileMode(ExePath, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
            }
        }

        if (!IsProvisioned)
        {
            throw new InvalidOperationException($"SteamCMD bootstrap extracted but {ExePath} is missing.");
        }

        return ExePath;
    }

    /// <summary>ZipArchive needs a seekable stream; the HTTP body isn't one.</summary>
    private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct).ConfigureAwait(false);
        ms.Position = 0;
        return ms;
    }
}
