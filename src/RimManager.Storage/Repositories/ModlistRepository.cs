using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Storage.Persistence;

namespace RimManager.Storage.Repositories;

/// <summary>
/// Persists <see cref="Modlist"/>s at the app root — <c>modlists/&lt;id&gt;.json</c>, with
/// snapshots under <c>snapshots/&lt;modlistId&gt;/</c>.
/// <para>
/// At the ROOT, not inside an instance folder: modlists replace instances as the app's
/// primary unit (the modlist migration). There is one install, so there is one set
/// of lists.
/// </para>
/// </summary>
public sealed class ModlistRepository
{
    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly string _root;
    private readonly string _modlistsDir;
    private readonly string _snapshotsRoot;
    private readonly JsonDocumentStore<Modlist> _store;
    private readonly JsonDocumentStore<ModlistSnapshot> _snapshots;

    public ModlistRepository(IFileSystem fs, string? root = null, IClock? clock = null)
    {
        _fs = fs;
        _clock = clock ?? SystemClock.Instance;
        _root = root ?? AppPaths.Root;
        _modlistsDir = Path.Combine(_root, "modlists");
        _snapshotsRoot = Path.Combine(_root, "snapshots");
        _store = new JsonDocumentStore<Modlist>(fs);
        _snapshots = new JsonDocumentStore<ModlistSnapshot>(fs);
    }

    public string ModlistsDirectory => _modlistsDir;

    /// <summary>Where a list's snapshots live. Public so migration can move files wholesale
    /// rather than round-tripping every snapshot through the serializer.</summary>
    public string SnapshotDirectory(string modlistId) => Path.Combine(_snapshotsRoot, modlistId);

    private string FileFor(string id) => Path.Combine(_modlistsDir, id + ".json");

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public IReadOnlyList<Modlist> List()
    {
        var result = new List<Modlist>();
        if (!_fs.DirectoryExists(_modlistsDir)) return result;

        foreach (var entry in _fs.EnumerateEntries(_modlistsDir)
                     .Where(e => !e.IsDirectory
                                 && e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            if (_store.Load(entry.FullPath) is { } list) result.Add(list);
        }

        return result.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Modlist? Get(string id) => _store.Load(FileFor(id));

    public Modlist? FindByName(string name) =>
        List().FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public Task SaveAsync(Modlist list, CancellationToken ct = default) =>
        _store.SaveAsync(FileFor(list.Id), list with { ModifiedUtc = _clock.UtcNow }, ct: ct);

    public async Task<Modlist> CreateAsync(
        string name,
        ModlistState? state = null,
        bool isDefault = false,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var list = new Modlist
        {
            Id = NewId(),
            Name = name.Trim(),
            IsDefault = isDefault,
            CreatedUtc = now,
            ModifiedUtc = now,
            State = state ?? ModlistState.Empty,
        };

        await _store.SaveAsync(FileFor(list.Id), list, ct: ct).ConfigureAwait(false);
        return list;
    }

    /// <summary>
    /// Deletes a list and its snapshot history. Refuses the default and the last one
    /// standing — the caller is expected to have hidden the control, but the invariant
    /// is enforced here too, because a rule that only exists in the UI is a rule the CLI
    /// does not have.
    /// </summary>
    public bool Delete(string id)
    {
        var all = List();
        if (all.FirstOrDefault(l => l.Id == id) is not { } list) return false;
        if (!DefaultModlist.CanDelete(list, all.Count)) return false;

        var file = FileFor(id);
        if (_fs.FileExists(file)) _fs.DeleteFile(file);

        var snapshots = SnapshotDirectory(id);
        if (_fs.DirectoryExists(snapshots)) _fs.DeleteDirectory(snapshots, recursive: true);

        return true;
    }

    /// <summary>
    /// Applies the "exactly one undeletable default" invariant, persisting whatever it
    /// changed, and seeding a default when there are no lists at all.
    /// <para>
    /// Called on every load. <see cref="DefaultModlist.Reconcile"/> returns an empty
    /// change set in the overwhelmingly common case, so the steady state costs a
    /// directory listing and no writes.
    /// </para>
    /// </summary>
    /// <param name="seed">
    /// Produces the arrangement for a freshly seeded default — in practice the game's
    /// live <c>ModsConfig.xml</c>, so the first list is never empty. Only called when
    /// there is nothing at all.
    /// </param>
    public async Task<IReadOnlyList<Modlist>> EnsureDefaultAsync(
        Func<ModlistState>? seed = null, CancellationToken ct = default)
    {
        var reconciliation = DefaultModlist.Reconcile(List());

        if (reconciliation.NeedsSeeding)
        {
            var seeded = await CreateAsync(
                DefaultModlist.SeedName, seed?.Invoke(), isDefault: true, ct).ConfigureAwait(false);
            return [seeded];
        }

        foreach (var changed in reconciliation.Changed)
            await _store.SaveAsync(FileFor(changed.Id), changed, ct: ct).ConfigureAwait(false);

        return reconciliation.Lists;
    }

    /// <summary>
    /// Copies a list's arrangement under a new name. The copy is never the default and
    /// never locked: those are properties of the original's role, not of its contents.
    /// <para>
    /// Snapshots are deliberately NOT copied. They are the history of how the original got
    /// where it is, and attaching that story to a copy would make the copy claim a past it
    /// never had — the same reasoning that kept the scan cache out of instance duplication.
    /// </para>
    /// </summary>
    public async Task<Modlist> DuplicateAsync(
        Modlist source, string newName, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var copy = source with
        {
            Id = NewId(),
            Name = newName.Trim(),
            IsDefault = false,
            Locked = false,
            CreatedUtc = now,
            ModifiedUtc = now,
            LastUsedUtc = null,

            // It has never been applied, so it has no evidence about the game's state.
            // Inheriting the original's hash would make drift detection confidently wrong.
            LastAppliedHash = null,
            LastAppliedUtc = null,
        };

        await _store.SaveAsync(FileFor(copy.Id), copy, ct: ct).ConfigureAwait(false);
        return copy;
    }

    /// <summary>
    /// Moves the default flag, demoting whoever held it. Nothing else could change it —
    /// <see cref="EnsureDefaultAsync"/> only repairs a broken set, it never reassigns a
    /// healthy one — so without this the first list to be marked default was default for
    /// ever, including the one migration happened to pick.
    /// </summary>
    public async Task<bool> SetDefaultAsync(string id, CancellationToken ct = default)
    {
        var all = List();
        if (all.All(l => l.Id != id)) return false;

        foreach (var list in all)
        {
            var shouldBe = list.Id == id;
            if (list.IsDefault == shouldBe) continue;

            await _store.SaveAsync(FileFor(list.Id), list with { IsDefault = shouldBe }, ct: ct)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>The list to open on startup: the most recently used, else the default.</summary>
    public Modlist? Selected(IReadOnlyList<Modlist> lists) =>
        lists.Where(l => l.LastUsedUtc is not null)
             .OrderByDescending(l => l.LastUsedUtc)
             .FirstOrDefault()
        ?? lists.FirstOrDefault(l => l.IsDefault)
        ?? lists.FirstOrDefault();

    // --- snapshots (2d) ------------------------------------------------------
    //
    // Ported from ProfileRepository, which the History tab still used after everything
    // else had moved. That left the app writing snapshots into instances/<id>/ — a tree
    // it had already migrated away from — so History described the INSTANCE while the
    // rest of the window described a modlist, and switching lists did not change it.
    //
    // Snapshots belong to a modlist: it is the thing whose arrangement they capture.

    public const int DefaultSnapshotKeep = 20;

    private string SnapshotFile(string modlistId, string snapshotId) =>
        Path.Combine(SnapshotDirectory(modlistId), snapshotId + ".json");

    /// <summary>
    /// Appends a snapshot of this list's current arrangement, then prunes to
    /// <paramref name="keep"/> unprotected entries.
    /// </summary>
    public async Task<ModlistSnapshot> SnapshotAsync(
        Modlist modlist, string reason, int keep = DefaultSnapshotKeep, string? name = null,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Ticks first, so the directory sorts chronologically by filename and the prune
        // needs no parse.
        var id = $"{now.UtcTicks:D19}-{Guid.NewGuid().ToString("N")[..8]}";

        var snapshot = new ModlistSnapshot
        {
            Id = id,
            ModlistId = modlist.Id,
            TakenUtc = now,
            Reason = reason,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            State = modlist.State,
        };

        await _snapshots.SaveAsync(SnapshotFile(modlist.Id, id), snapshot, ct: ct).ConfigureAwait(false);
        Prune(modlist.Id, keep);
        return snapshot;
    }

    /// <summary>Newest first, which is the order History shows them in.</summary>
    public IReadOnlyList<ModlistSnapshot> ListSnapshots(string modlistId)
    {
        var dir = SnapshotDirectory(modlistId);
        var result = new List<ModlistSnapshot>();
        if (!_fs.DirectoryExists(dir)) return result;

        foreach (var entry in _fs.EnumerateEntries(dir)
                     .Where(e => !e.IsDirectory
                                 && e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            if (_snapshots.Load(entry.FullPath) is { } s) result.Add(s);
        }

        return result.OrderByDescending(s => s.TakenUtc).ToList();
    }

    /// <summary>
    /// On-disk size of each snapshot, by id — History's SIZE column. Read from the
    /// directory entry rather than estimated from the state, because the column's job is
    /// to explain why 48 snapshots cost 3.1 MB, and a figure derived from the mod count
    /// would answer a different question convincingly.
    /// </summary>
    public IReadOnlyDictionary<string, long> SnapshotSizes(string modlistId)
    {
        var dir = SnapshotDirectory(modlistId);
        if (!_fs.DirectoryExists(dir)) return new Dictionary<string, long>();

        return _fs.EnumerateEntries(dir)
            .Where(e => !e.IsDirectory
                        && e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => Path.GetFileNameWithoutExtension(e.FullPath), e => e.Size);
    }

    public ModlistSnapshot? GetSnapshot(string modlistId, string snapshotId) =>
        _snapshots.Load(SnapshotFile(modlistId, snapshotId));

    /// <summary>
    /// Restores a snapshot. History is <b>append-only</b> (non-negotiable #5): this
    /// appends a new snapshot whose contents equal the old one and moves the list onto
    /// it — nothing is rewound, and the state you restored <em>from</em> stays in the list.
    /// </summary>
    public async Task<Modlist> RestoreSnapshotAsync(
        Modlist modlist, string snapshotId, CancellationToken ct = default)
    {
        var snapshot = GetSnapshot(modlist.Id, snapshotId)
            ?? throw new InvalidOperationException($"Snapshot '{snapshotId}' not found.");

        var restored = modlist with { State = snapshot.State, ModifiedUtc = _clock.UtcNow };

        await SnapshotAsync(restored, $"restored {snapshotId}", ct: ct).ConfigureAwait(false);
        await _store.SaveAsync(FileFor(restored.Id), restored, ct: ct).ConfigureAwait(false);
        return restored;
    }

    /// <summary>
    /// Names a snapshot, which is what makes it survive pruning.
    /// <para>
    /// <b>Null and empty mean different things here.</b> <c>null</c> is "leave the name
    /// alone"; an empty or whitespace string is "clear it". Passing null intending to
    /// clear is how un-naming was briefly impossible.
    /// </para>
    /// </summary>
    public async Task<ModlistSnapshot> AnnotateSnapshotAsync(
        string modlistId, string snapshotId, string? name = null,
        CancellationToken ct = default)
    {
        var snapshot = GetSnapshot(modlistId, snapshotId)
            ?? throw new InvalidOperationException($"Snapshot '{snapshotId}' not found.");

        var updated = snapshot with
        {
            Name = name is null
                ? snapshot.Name
                : string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
        };

        await _snapshots.SaveAsync(SnapshotFile(modlistId, snapshotId), updated, ct: ct)
            .ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Drops every UNPROTECTED snapshot — Settings ▸ Advanced ▸ danger zone. Named states
    /// are exempt here too, and that is not a half-measure: the whole meaning of naming a
    /// state is that it outlives the rolling window, so a "delete all" that took them
    /// would make naming worthless. The confirmation says which survive.
    /// </summary>
    public int DeleteUnprotectedSnapshots(string modlistId)
    {
        var removed = 0;
        foreach (var snapshot in ListSnapshots(modlistId))
        {
            if (snapshot.IsProtected) continue;
            _fs.DeleteFile(SnapshotFile(modlistId, snapshot.Id));
            removed++;
        }

        return removed;
    }

    /// <summary>How many are named, and so exempt from any prune.</summary>
    public int ProtectedSnapshotCount(string modlistId) =>
        ListSnapshots(modlistId).Count(s => s.IsProtected);

    /// <summary>Drops snapshots older than <paramref name="age"/>. Protected ones never go.</summary>
    public int PruneOlderThan(string modlistId, TimeSpan age)
    {
        var cutoff = _clock.UtcNow - age;
        var removed = 0;

        foreach (var snapshot in ListSnapshots(modlistId))
        {
            if (snapshot.IsProtected || snapshot.TakenUtc >= cutoff) continue;
            _fs.DeleteFile(SnapshotFile(modlistId, snapshot.Id));
            removed++;
        }

        return removed;
    }

    private void Prune(string modlistId, int keep)
    {
        var stale = ListSnapshots(modlistId)   // newest first
            .Where(s => !s.IsProtected)
            .Skip(keep)
            .ToList();

        foreach (var snapshot in stale) _fs.DeleteFile(SnapshotFile(modlistId, snapshot.Id));
    }

    /// <summary>Stamps LAST USED. Separate from <see cref="SaveAsync"/> so switching a
    /// list does not rewrite its arrangement.</summary>
    public async Task MarkUsedAsync(Modlist list, CancellationToken ct = default)
    {
        if (Get(list.Id) is not { } current) return;
        await _store.SaveAsync(
            FileFor(list.Id), current with { LastUsedUtc = _clock.UtcNow }, ct: ct).ConfigureAwait(false);
    }
}
