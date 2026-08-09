using RimManager.Core.Workshop;

namespace RimManager.Integrations.SteamCmd;

/// <summary>What happened to one requested item: downloaded + relocated, or why not.</summary>
public sealed record WorkshopInstallOutcome(string PublishedFileId, bool Installed, string? Path, string? Error);

/// <summary>
/// Downloads Workshop items anonymously via SteamCMD and relocates each into the game's
/// Mods folder. The one place the download→install flow lives, shared by the CLI
/// (<c>workshop download</c> / <c>collection --download</c>) and the App — so there's a
/// single audited copy of the cross-volume relocate.
/// </summary>
public sealed class WorkshopDownloadService
{
    /// <summary>
    /// Downloads all <paramref name="ids"/> in one anonymous SteamCMD session into
    /// <paramref name="stagingDir"/>, then copies each successful item into
    /// <paramref name="modsDir"/> as a folder named by its id. Returns a per-id outcome.
    /// </summary>
    public async Task<IReadOnlyList<WorkshopInstallOutcome>> DownloadAndInstallAsync(
        IReadOnlyList<string> ids,
        string steamcmdExe,
        string modsDir,
        string stagingDir,
        Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var runner = new SteamCmdRunner(steamcmdExe);
        var run = await runner
            .DownloadWorkshopItemsAsync(ids, SteamWorkshopClient.RimWorldAppId, stagingDir, onLine, ct)
            .ConfigureAwait(false);

        Directory.CreateDirectory(modsDir);
        var outcomes = new List<WorkshopInstallOutcome>(ids.Count);
        foreach (var id in ids)
        {
            var result = run.Results.FirstOrDefault(r => r.PublishedFileId == id);
            if (result is not { Success: true, DownloadedPath: { } src } || !Directory.Exists(src))
            {
                outcomes.Add(new WorkshopInstallOutcome(id, false, null, result?.Error ?? "not downloaded"));
                continue;
            }

            var dest = Path.Combine(modsDir, id);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            CopyDirectory(src, dest);
            outcomes.Add(new WorkshopInstallOutcome(id, true, dest, null));
        }

        return outcomes;
    }

    /// <summary>Recursive copy — SteamCMD's cache and the game are often on different volumes,
    /// so a rename/move won't do.</summary>
    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(src, dest));
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(src, dest), overwrite: true);
        }
    }
}
