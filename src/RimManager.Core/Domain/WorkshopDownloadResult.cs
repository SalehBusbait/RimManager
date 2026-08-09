namespace RimManager.Core.Domain;

/// <summary>
/// The outcome of a single <c>workshop_download_item</c> from SteamCMD, extracted from
/// its console output. On success, <see cref="DownloadedPath"/> is where SteamCMD placed
/// the item (under its own <c>steamapps/workshop/content/294100</c>), which the caller
/// then relocates into the game's <c>Mods</c> folder.
/// </summary>
public sealed record WorkshopDownloadResult
{
    public required string PublishedFileId { get; init; }

    public required bool Success { get; init; }

    /// <summary>Absolute path SteamCMD reported for the downloaded item, on success.</summary>
    public string? DownloadedPath { get; init; }

    public long? Bytes { get; init; }

    /// <summary>SteamCMD's failure reason (e.g. <c>Failure</c>, <c>Timeout</c>), on failure.</summary>
    public string? Error { get; init; }
}
