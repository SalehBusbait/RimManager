using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimManager.App.ViewModels;
using RimManager.Core.Abstractions;
using RimManager.Core.Workshop;
using RimManager.Integrations.SteamCmd;
using RimManager.Storage;

namespace RimManager.App.Services;

/// <summary>
/// Measures what Settings ▸ Integrations reports: whether Steam is up, how many Workshop
/// items it has actually installed, whether RimManager's private SteamCMD is provisioned
/// and how large it is, and git's version and path.
/// <para>
/// The git <i>counts</i> are passed in rather than measured here: the scan has already
/// probed which mods are working trees and read their state, and probing twice would
/// both cost process launches and let the page disagree with the ⎇ glyphs on the rows.
/// </para>
/// </summary>
public sealed class IntegrationStatusService(IFileSystem fs, GitService git)
{
    private readonly IFileSystem _fs = fs;
    private readonly GitService _git = git;

    public async Task<IntegrationStatus> LoadAsync(
        string? workshopDir, int trackedRepos, int dirtyRepos, CancellationToken ct = default)
    {
        var running = new SteamClientDetector().IsClientRunning();
        var installedItems = CountInstalledWorkshopItems(workshopDir);

        var provisioner = new SteamCmdProvisioner(AppPaths.SteamCmdDir);
        var provisioned = provisioner.IsProvisioned;
        var bytes = provisioned ? DirectorySize(provisioner.InstallDir) : 0;

        var version = await _git.VersionAsync(ct).ConfigureAwait(false);
        var path = version is null ? null : _git.ResolveGitPath();

        return new IntegrationStatus(
            running, installedItems, provisioned, provisioner.InstallDir, bytes,
            version, path, trackedRepos, dirtyRepos);
    }

    /// <summary>
    /// Provisions SteamCMD on demand — the card's Install button. The same provisioner
    /// the Collection tab's download uses, so there is one copy of SteamCMD and one
    /// place that fetches it.
    /// </summary>
    public Task<string> InstallSteamCmdAsync(CancellationToken ct = default) =>
        new SteamCmdProvisioner(AppPaths.SteamCmdDir).EnsureProvisionedAsync(ct);

    /// <summary>
    /// Items in Steam's <c>appworkshop_294100.acf</c>, or null when there is no manifest.
    /// Null and zero are different answers: "Steam has never recorded anything here" is
    /// not "you have nothing installed".
    /// </summary>
    private int? CountInstalledWorkshopItems(string? workshopContentDir)
    {
        if (workshopContentDir is null) return null;

        // The manifest sits two levels above the content dir:
        // <library>/steamapps/workshop/appworkshop_294100.acf, content at
        // <library>/steamapps/workshop/content/294100.
        var root = Path.GetDirectoryName(Path.GetDirectoryName(workshopContentDir.TrimEnd('/', '\\')));
        if (root is null) return null;

        var acf = Path.Combine(root, $"appworkshop_{SteamWorkshopClient.RimWorldAppId}.acf");
        if (!_fs.FileExists(acf)) return null;

        try { return WorkshopManifestParser.Parse(_fs.ReadAllText(acf)).Items.Count; }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// Size of a provisioned SteamCMD. Best-effort: a file that vanishes mid-walk — its
    /// own self-update does exactly that — must yield a number, not an exception.
    /// Sizes come from <see cref="IFileSystem.EnumerateEntries"/>, which stats without
    /// opening, so this stays cheap enough to run whenever the page is opened.
    /// </summary>
    private long DirectorySize(string dir)
    {
        var total = 0L;
        try
        {
            foreach (var entry in EnumerateFiles(dir)) total += entry.Size;
        }
        catch (IOException)
        {
            // Report what we counted rather than nothing.
        }

        return total;
    }

    private IEnumerable<FileEntry> EnumerateFiles(string dir)
    {
        foreach (var entry in _fs.EnumerateEntries(dir))
        {
            if (entry.IsDirectory)
            {
                foreach (var nested in EnumerateFiles(entry.FullPath)) yield return nested;
            }
            else
            {
                yield return entry;
            }
        }
    }
}
