using System.Globalization;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;

namespace RimManager.Core.Writing;

/// <summary>Outcome of an apply attempt.</summary>
/// <param name="Written">True if the file was written.</param>
/// <param name="BackupPath">Path of the timestamped backup, if one was made.</param>
/// <param name="Message">Human-readable status.</param>
public sealed record ApplyResult(bool Written, string? BackupPath, string Message);

/// <summary>
/// Writes <c>ModsConfig.xml</c> safely (spec §3): refuse while RimWorld runs,
/// back up first, write atomically. The single choke point for the one file we
/// mutate.
/// </summary>
public sealed class ApplyService
{
    /// <summary>
    /// How many ModsConfig backups to keep. Matches <c>PhysicalFileSystem.BackupsKept</c>
    /// — the sibling-file mechanism this replaces pruned to the same depth, and changing
    /// the number while moving the folder would hide one change inside another.
    /// </summary>
    public const int BackupsKept = 20;

    private readonly IFileSystem _fs;
    private readonly IGameProcessDetector _gameDetector;
    private readonly IClock _clock;

    public ApplyService(
        IFileSystem fs, IGameProcessDetector? gameDetector = null, IClock? clock = null)
    {
        _fs = fs;
        _gameDetector = gameDetector ?? NeverRunningGameDetector.Instance;
        _clock = clock ?? SystemClockFallback.Instance;
    }

    /// <summary>
    /// Writes <paramref name="config"/> to <paramref name="modsConfigPath"/>. Returns
    /// a non-written result (rather than throwing) when the game is running, so the
    /// caller can report it cleanly.
    /// </summary>
    /// <param name="backupDirectory">
    /// Where to keep the timestamped copy of the previous <c>ModsConfig.xml</c> (O5).
    /// <para>
    /// When null the backup lands beside the file, which is <b>RimWorld's own config
    /// folder</b> — the previous behaviour, and the reason this parameter exists. Our
    /// backups accumulated inside a directory the game owns and Steam validates, next to
    /// the file they are copies of, where they look to any other tool exactly like more
    /// config. RimManager's own folder is where RimManager's files belong.
    /// </para>
    /// </param>
    public async Task<ApplyResult> ApplyAsync(
        string modsConfigPath, ModsConfig config,
        string? backupDirectory = null, CancellationToken ct = default)
    {
        if (_gameDetector.IsGameRunning())
        {
            return new ApplyResult(false, null,
                "RimWorld is running — refusing to write ModsConfig.xml. Close the game and retry.");
        }

        var bytes = ModsConfigWriter.SerializeToBytes(config);

        // Ours first, then a write that makes none of its own: two backups of one file,
        // in two places, is worse than either.
        var backupPath = backupDirectory is null
            ? null
            : await BackUpAsync(modsConfigPath, backupDirectory, ct).ConfigureAwait(false);

        var sibling = await _fs
            .AtomicWriteAsync(modsConfigPath, bytes, backup: backupDirectory is null, ct)
            .ConfigureAwait(false);

        backupPath ??= sibling;

        return new ApplyResult(true, backupPath,
            backupPath is null ? "Wrote ModsConfig.xml." : $"Wrote ModsConfig.xml (backup: {backupPath}).");
    }

    /// <summary>
    /// Copies the existing ModsConfig.xml into <paramref name="directory"/>, then prunes
    /// to <see cref="BackupsKept"/>. Returns null when there was nothing to copy — a
    /// first Apply against a config that does not exist yet is normal, not a failure.
    /// </summary>
    private async Task<string?> BackUpAsync(string modsConfigPath, string directory, CancellationToken ct)
    {
        if (!_fs.FileExists(modsConfigPath)) return null;

        _fs.CreateDirectory(directory);

        // The game's own name, plus when: "ModsConfig.20260808T213000Z.xml" sorts
        // chronologically and still opens in anything that reads XML. A ".bak" suffix
        // would not.
        var stamp = _clock.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var target = Path.Combine(directory, $"ModsConfig.{stamp}.xml");

        byte[] existing;
        using (var source = _fs.OpenRead(modsConfigPath))
        using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, ct).ConfigureAwait(false);
            existing = buffer.ToArray();
        }

        // backup: false — this IS the backup; a backup of a backup is noise.
        await _fs.AtomicWriteAsync(target, existing, backup: false, ct).ConfigureAwait(false);

        Prune(directory);
        return target;
    }

    private void Prune(string directory)
    {
        var stale = _fs.EnumerateEntries(directory)
            .Where(e => !e.IsDirectory)
            .Where(e => Path.GetFileName(e.FullPath).StartsWith("ModsConfig.", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.FullPath, StringComparer.Ordinal)   // the stamp sorts
            .Skip(BackupsKept)
            .ToList();

        foreach (var entry in stale) _fs.DeleteFile(entry.FullPath);
    }

    /// <summary>
    /// The clock used when a caller supplies none. Core takes time through
    /// <see cref="IClock"/>, and this keeps the parameter optional so the two CLI call
    /// sites and the tests are not forced to invent one.
    /// </summary>
    private sealed class SystemClockFallback : IClock
    {
        public static readonly SystemClockFallback Instance = new();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    /// <summary>Builds a new config that keeps the existing version + expansions but sets a new active order.</summary>
    public static ModsConfig WithActiveOrder(ModsConfig existing, IEnumerable<ModId> newOrder) =>
        existing with { ActiveMods = [.. newOrder] };
}
