using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RimManager.Core.Abstractions;
using RimManager.Core.Github;
using RimManager.Integrations.Updates;
using RimManager.Storage;

namespace RimManager.App.Services;

/// <summary>
/// The app's own update story, orchestrated: ask GitHub for releases, let the
/// advisor judge them against the running version, and — on request or by the
/// auto-install preference — fetch the installer and hand over to it.
/// </summary>
/// <remarks>
/// The check never blocks startup and treats every network failure as "no news":
/// an update notice is the least important thing in the window, and the one thing
/// it must never do is stop the manager from managing.
/// </remarks>
public sealed class AppUpdateService(IHttpFetcher fetcher)
{
    // ProjectUrl is a compile-time constant that names this repo; if it ever stops
    // parsing, the loudest possible failure is the right one.
    private static readonly GitHubRepoRef Repo =
        GitHubRepoRef.TryParse(ViewModels.AboutViewModel.ProjectUrl, out var repo)
            ? repo
            : throw new InvalidOperationException("ProjectUrl is not a GitHub repository URL.");

    private readonly GitHubReleasesClient _client = new(fetcher);
    private readonly AppUpdater _updater = new();

    /// <summary>The verdict of the last check; null means up to date or unchecked.</summary>
    public AppUpdateAdvice? Available { get; private set; }

    /// <summary>The version this binary reports, pre-release suffix intact.</summary>
    public static string? CurrentVersion => Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    /// True when running the copy the Setup exe installed — the only case where
    /// "Update now" can mean an in-place upgrade rather than a second install.
    /// </summary>
    public bool CanInstallInPlace => _updater.IsInstalledViaSetup();

    /// <summary>Checks quietly. Returns the advice, remembering it either way.</summary>
    public async Task<AppUpdateAdvice?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var releases = await _client.GetReleasesAsync(Repo, ct: ct).ConfigureAwait(false);
            return Available = AppUpdateAdvisor.Advise(CurrentVersion, releases);
        }
        catch
        {
            // Offline, rate-limited, DNS-less: all read as "no news today".
            return Available = null;
        }
    }

    /// <summary>
    /// Downloads the installer and starts it silently. The caller must close the
    /// application immediately after this returns true — the installer waits for
    /// the process, and its fixed AppId makes the run an in-place upgrade.
    /// </summary>
    public async Task<bool> DownloadAndRunInstallerAsync(
        AppUpdateAdvice advice, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (advice.Installer is null || !CanInstallInPlace) return false;

        var path = await _updater.DownloadAsync(
            advice.Installer.DownloadUrl, advice.Installer.Name, progress, ct).ConfigureAwait(false);
        _updater.RunInstaller(path);
        return true;
    }

    /// <summary>The fallback for portable and non-Windows copies: the release page.</summary>
    public void OpenReleasePage(AppUpdateAdvice advice)
    {
        if (!string.IsNullOrEmpty(advice.PageUrl)) new ShellUriLauncher().Launch(advice.PageUrl);
    }
}
