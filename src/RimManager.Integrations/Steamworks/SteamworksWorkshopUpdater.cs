using System.Diagnostics;
using System.Globalization;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.Integrations.Steamworks;

/// <summary>
/// The parent half of the Steamworks-native <see cref="IWorkshopUpdater"/>: spawns a
/// short-lived child process (our own exe with <see cref="SteamworksDownload.ArgumentMarker"/>)
/// to ask the client for each item, then watches Steam's own
/// <c>appworkshop_294100.acf</c> until each item's installed timestamp reaches the
/// remote publish time. The client does the downloading and keeps its own
/// bookkeeping — we never touch the bytes, so there is no second writer to fight it.
/// </summary>
/// <remarks>
/// <para><b>A bare <c>DownloadItem</c>, not a resubscribe.</b> This used to
/// unsubscribe and resubscribe, adopted from RimSort on a finding that turned out to
/// have been measured through two defects that have since been fixed — see
/// <see cref="SteamworksDownload"/> for the retest and its evidence. The plain
/// download is proven live here, and it removes the resubscribe's hazard: there is no
/// window in which the user is unsubscribed from their own mods.</para>
/// <para><b>Why a child process and an external watch</b> — two separate lessons,
/// both learned live. A session against the game's app id reads as "RimWorld is
/// running" and Steam pauses downloads during gameplay by default; holding the
/// session to poll <c>GetItemState</c> therefore deadlocked. Worse,
/// <c>SteamAPI_Shutdown()</c> does not clear the running state — the client watches
/// the <i>process</i> — so even a seconds-short in-app session kept the downloads
/// deferred until the whole app closed. The session lives in a child that exits
/// (Steam sees "game closed" seconds later, app stays open), and completion is
/// observed here in the acf, by the exact timestamp comparison the update check is
/// built on.</para>
/// </remarks>
public sealed class SteamworksWorkshopUpdater(
    string selfExecutable, string gameDirectory, int appId, string? workshopContentDir)
    : IWorkshopUpdater
{
    private const int PollSeconds = 2;
    private const int WatchTimeoutMinutes = 10;
    private const int ChildTimeoutSeconds = 90;

    public async Task<IReadOnlyList<WorkshopUpdateResult>> UpdateAsync(
        IReadOnlyList<WorkshopUpdateRequest> items,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var baseline = ReadManifest();

        progress?.Report("Asking Steam to download the current version…");
        await RunChildAsync(items, cancellationToken);

        // --- Watch. The acf is Steam's own record of the installed version; an
        // item is done when its timestamp reaches the remote publish time (or moves
        // at all, when the caller had no target). Mid-resubscribe the entry can
        // vanish from the manifest entirely — that reads as "not finished", never
        // as an error.
        // Mid-download the entry can be rewritten; a torn read reads as "not
        // finished", never as an error.
        var done = new HashSet<string>();
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(WatchTimeoutMinutes);

        while (done.Count < items.Count && DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(PollSeconds), cancellationToken);

            var manifest = ReadManifest();
            foreach (var item in items)
            {
                if (done.Contains(item.PublishedFileId)) continue;

                var installed = manifest?.TryGet(item.PublishedFileId)?.TimeUpdatedUtc;
                var before = baseline?.TryGet(item.PublishedFileId)?.TimeUpdatedUtc;
                var finished = item.RemoteUpdatedUtc is { } target
                    ? installed >= target
                    : installed is not null && (before is null || installed > before);
                if (finished) done.Add(item.PublishedFileId);
            }

            progress?.Report(items.Count == 1
                ? "Waiting for Steam to finish the download…"
                : $"Waiting for Steam · {done.Count}/{items.Count} updated…");
        }

        return
        [
            .. items.Select(i => done.Contains(i.PublishedFileId)
                ? new WorkshopUpdateResult(i.PublishedFileId, true)
                : new WorkshopUpdateResult(i.PublishedFileId, false,
                    "Steam had not finished this download when we stopped watching — "
                    + "it may still complete on its own.")),
        ];
    }

    /// <summary>Runs the download child and maps its exit code. The child's exit
    /// is not a formality: it is what makes Steam see "game closed" and start the
    /// downloads while the app stays open.</summary>
    private async Task RunChildAsync(
        IReadOnlyList<WorkshopUpdateRequest> items, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = selfExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add(SteamworksDownload.ArgumentMarker);
        info.ArgumentList.Add(gameDirectory);
        info.ArgumentList.Add(appId.ToString(CultureInfo.InvariantCulture));
        foreach (var item in items) info.ArgumentList.Add(item.PublishedFileId);

        using var child = Process.Start(info)
            ?? throw new InvalidOperationException("The Steam request helper failed to start.");

        // Started BEFORE the wait, and without the timeout token. Before, because a
        // batch's worth of report lines can fill the pipe buffer and deadlock a child
        // nobody is reading; without the token, because after a Kill the pipe closes
        // and this completes on its own — awaiting it cannot then hang. (The sibling
        // collection exporter awaits its read to completion *first*, which is why its
        // timeout can never fire. Not copied.)
        var report = child.StandardOutput.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ChildTimeoutSeconds));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { child.Kill(); } catch { }
            throw new InvalidOperationException("The Steam request helper did not finish.");
        }

        // Always awaited, not only on the failure path: the child has exited, so the
        // pipe is closed and this returns at once, and nothing is left dangling.
        var detail = (await report).Replace(Environment.NewLine, " · ").Trim(' ', '·');

        var failure = child.ExitCode switch
        {
            SteamworksDownload.Ok => null,
            SteamworksDownload.ClientUnreachable =>
                "Couldn't reach the Steam client — is Steam running and logged in?",
            SteamworksDownload.NoSteamApiLibrary =>
                "The game install has no Steamworks library — a non-Steam copy can't drive Workshop updates.",
            SteamworksDownload.DownloadRefused =>
                "Steam refused the download — the item may be delisted, or no longer subscribed.",
            _ => $"The Steam request helper failed (exit {child.ExitCode}).",
        };

        // The child's per-item state lines travel with the failure. A helper that can
        // only return a number is how a call that never reached the API went unnoticed
        // through two commits and one wrong conclusion.
        if (failure is not null)
            throw new InvalidOperationException(
                detail.Length == 0 ? failure : $"{failure} ({detail})");
    }

    /// <summary>The acf sits two levels above the content dir — the same resolution
    /// the update check uses. Null (no dir, no file, or a torn mid-write read) means
    /// "no answer this round", never a crash.</summary>
    private WorkshopInstallState? ReadManifest()
    {
        if (workshopContentDir is null) return null;
        var root = Path.GetDirectoryName(Path.GetDirectoryName(workshopContentDir.TrimEnd('/', '\\')));
        if (root is null) return null;

        var acf = Path.Combine(root, $"appworkshop_{appId}.acf");
        try
        {
            return File.Exists(acf) ? WorkshopManifestParser.Parse(File.ReadAllText(acf)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
