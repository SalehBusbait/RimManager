using System.Diagnostics;
using System.Globalization;

namespace RimManager.Integrations.Steamworks;

/// <summary>What the child reported back: the collection's id, or why not.</summary>
public sealed record CollectionExportResult(
    ulong? CollectionId, string? Error, bool LegalAgreementPending, string? AddedNote);

/// <summary>
/// The parent half of "Export as Steam collection" (NF-10 slice 4): spawns the
/// <see cref="SteamworksCollectionCreate"/> child and reads the created collection's
/// id off its stdout — the one Steamworks child that must hand a value back, because
/// the whole point is opening the new collection's page for the user to review.
/// </summary>
public sealed class SteamworksCollectionExporter(
    string selfExecutable, string gameDirectory, int appId)
{
    private const int ChildTimeoutSeconds = 120;

    public async Task<CollectionExportResult> CreateAsync(
        string title, IReadOnlyList<string> publishedFileIds,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = selfExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add(SteamworksCollectionCreate.ArgumentMarker);
        info.ArgumentList.Add(gameDirectory);
        info.ArgumentList.Add(appId.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(title);
        foreach (var id in publishedFileIds) info.ArgumentList.Add(id);

        using var child = Process.Start(info)
            ?? throw new InvalidOperationException("The Steam collection helper failed to start.");

        var stdout = await child.StandardOutput.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ChildTimeoutSeconds));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { child.Kill(); } catch { }
            return new CollectionExportResult(null, "The Steam collection helper did not finish.", false, null);
        }

        ulong? collectionId = null;
        var legalPending = false;
        string? added = null;
        string? eresult = null;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("collection=", StringComparison.Ordinal)
                && ulong.TryParse(line["collection=".Length..], out var id))
                collectionId = id;
            else if (line.StartsWith("added=", StringComparison.Ordinal))
                added = line["added=".Length..];
            else if (line.StartsWith("eresult=", StringComparison.Ordinal))
                eresult = line["eresult=".Length..];
            else if (line == "legal-agreement=pending")
                legalPending = true;
        }

        var error = child.ExitCode switch
        {
            SteamworksCollectionCreate.Ok => null,
            SteamworksCollectionCreate.ClientUnreachable =>
                "Couldn't reach the Steam client — is Steam running and logged in?",
            SteamworksCollectionCreate.NoSteamApiLibrary =>
                "The game install has no Steamworks library — a non-Steam copy can't create collections.",
            SteamworksCollectionCreate.MissingExport =>
                "The game's Steamworks library doesn't offer the collection APIs.",
            SteamworksCollectionCreate.CreateFailed =>
                $"Steam refused to create the collection (result {eresult ?? "?"}).",
            SteamworksCollectionCreate.SubmitFailed =>
                $"The collection was created but naming it failed (result {eresult ?? "?"}).",
            SteamworksCollectionCreate.TimedOut =>
                "Steam did not answer in time.",
            _ => $"The Steam collection helper failed (exit {child.ExitCode}).",
        };

        return new CollectionExportResult(collectionId, error, legalPending, added);
    }
}
