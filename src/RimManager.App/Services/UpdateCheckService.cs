using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.App.Services;

/// <summary>
/// The App's bridge to Workshop update-checking: reads Steam's installed-version
/// manifest, fetches live metadata, and runs the pure <see cref="UpdateChecker"/>.
/// Thin I/O orchestration over already-tested Core pieces — the same pipeline the CLI
/// <c>workshop updates</c> command uses.
/// </summary>
public sealed class UpdateCheckService(IFileSystem fs, IHttpFetcher fetcher)
{
    private readonly IFileSystem _fs = fs;
    private readonly IHttpFetcher _fetcher = fetcher;

    /// <summary>
    /// Checks the given mods (only Workshop mods with a <c>PublishedFileId</c> are
    /// contacted) against the live Workshop, using <paramref name="workshopDir"/> to
    /// locate Steam's <c>appworkshop_294100.acf</c> for installed timestamps.
    /// </summary>
    public async Task<ImmutableArray<ModUpdateStatus>> CheckAsync(
        IReadOnlyList<Mod> mods, string? workshopDir, CancellationToken ct = default)
    {
        var workshopMods = mods
            .Where(m => m.Source == ModSource.Workshop && m.PublishedFileId is not null)
            .ToList();
        if (workshopMods.Count == 0) return [];

        var installed = ReadInstallState(workshopDir);
        var client = new SteamWorkshopClient(_fetcher);
        var remote = await client
            .GetByIdAsync(workshopMods.Select(m => m.PublishedFileId!), ct)
            .ConfigureAwait(false);

        return UpdateChecker.Check(workshopMods, installed, remote);
    }

    /// <summary>Locates and parses <c>appworkshop_294100.acf</c> (two levels up from the
    /// content dir), or returns empty state when it isn't present.</summary>
    private WorkshopInstallState ReadInstallState(string? workshopContentDir)
    {
        if (workshopContentDir is null) return WorkshopInstallState.Empty;

        var workshopRoot = Path.GetDirectoryName(Path.GetDirectoryName(workshopContentDir.TrimEnd('/', '\\')));
        if (workshopRoot is null) return WorkshopInstallState.Empty;

        var acf = Path.Combine(workshopRoot, $"appworkshop_{SteamWorkshopClient.RimWorldAppId}.acf");
        return _fs.FileExists(acf)
            ? WorkshopManifestParser.Parse(_fs.ReadAllText(acf))
            : WorkshopInstallState.Empty;
    }
}
