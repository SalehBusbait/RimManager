using System.Diagnostics;
using Microsoft.Win32;

namespace RimManager.Integrations.Updates;

/// <summary>
/// The side-effect half of in-app updating: fetch the installer, run it, and know
/// whether running it is even the right move on this machine. The decision of
/// WHETHER to update lives in Core's <c>AppUpdateAdvisor</c>; this class only acts.
/// </summary>
public sealed class AppUpdater(HttpClient? http = null)
{
    /// <summary>The installer's fixed AppId — what makes an upgrade land in place.</summary>
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C2F1A96-5B0E-4D43-9A67-D08B3A6C51E4}_is1";

    private readonly HttpClient _http = http ?? Shared;

    private static readonly HttpClient Shared = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "RimManager" } },
    };

    /// <summary>
    /// True when THIS copy was installed by the Setup exe, which is the only case
    /// where downloading a new Setup and running it silently is an upgrade rather
    /// than a second installation beside a portable copy.
    /// </summary>
    public bool IsInstalledViaSetup()
    {
        if (!OperatingSystem.IsWindows()) return false;

        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey)
            ?? Registry.LocalMachine.OpenSubKey(UninstallKey);
        return key is not null;
    }

    /// <summary>
    /// Streams the installer to a temp file and returns its path. Deliberately a
    /// fresh name per download — a half-written file from a killed session must
    /// never be mistaken for a complete installer.
    /// </summary>
    public async Task<string> DownloadAsync(
        string url, string fileName, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}.exe");

        using var response = await _http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(path);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (total is > 0) progress?.Report((double)done / total.Value);
        }

        return path;
    }

    /// <summary>
    /// Starts the installer silently. The CALLER must exit the application right
    /// after: the setup's CloseApplications handling waits on this process, and the
    /// fixed AppId turns the run into an in-place upgrade. /NORESTART because an
    /// update to a mod manager must never reboot anyone's machine.
    /// </summary>
    public void RunInstaller(string path) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = "/SILENT /NORESTART",
            UseShellExecute = true,
        });
}
