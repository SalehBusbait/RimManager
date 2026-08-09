using System.Collections.Generic;
using System.Linq;
using RimManager.Core.Scanning;

namespace RimManager.App.ViewModels;

/// <summary>
/// What the strip says, accumulated across polls.
/// <para>
/// <see cref="ModRootProbe"/> reports <em>deltas</em> — one mod this tick, another the next —
/// and the user needs the running total since they last rescanned. A Steam collection landing
/// mod by mod would otherwise flicker "1 mod added" over and over and never say twelve.
/// </para>
/// <para>
/// A mod that arrives and then leaves before the rescan <b>cancels</b>: the two facts are
/// news only relative to what is on screen, and nothing about the load order changed. Saying
/// "1 added, 1 removed" about the same folder would be arithmetic rather than information.
/// </para>
/// </summary>
public sealed class ModRootNotice
{
    private readonly HashSet<string> _added = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);

    public int AddedCount => _added.Count;
    public int RemovedCount => _removed.Count;

    /// <summary>True when there is something worth putting on screen.</summary>
    public bool HasNews => _added.Count > 0 || _removed.Count > 0;

    public void Record(ModRootChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var name in changes.Added)
        {
            // Back after being reported gone: the pair cancels rather than accumulating.
            if (!_removed.Remove(name)) _added.Add(name);
        }

        foreach (var name in changes.Removed)
        {
            if (!_added.Remove(name)) _removed.Add(name);
        }
    }

    /// <summary>Forgets everything — a rescan happened, or the user dismissed it.</summary>
    public void Clear()
    {
        _added.Clear();
        _removed.Clear();
    }

    /// <summary>
    /// "3 mods added on disk", "3 added, 1 removed on disk".
    /// <para>
    /// <b>Counts, never "changes detected".</b> The number is the whole value: nobody else
    /// ships it — it is an open, unanswered request against both Vortex and CurseForge — and
    /// a notice that cannot say how much changed is one the user has to go and check anyway,
    /// which is what they were doing before it existed.
    /// </para>
    /// </summary>
    public string Text
    {
        get
        {
            if (!HasNews) return string.Empty;

            var parts = new List<string>(2);
            if (_added.Count > 0) parts.Add($"{_added.Count} added");
            if (_removed.Count > 0) parts.Add($"{_removed.Count} removed");

            // "1 mod added on disk" reads better than "1 added on disk", and only the
            // single-fact case has room for the noun.
            var body = string.Join(", ", parts);
            if (parts.Count == 1 && _added.Count + _removed.Count == 1)
                body = body.Replace(" ", " mod ", StringComparison.Ordinal);

            return $"{body} on disk";
        }
    }

    /// <summary>The names, for the log line — a count on screen, the detail in the log.</summary>
    public string Detail =>
        string.Join(" · ",
            _added.Select(n => "+" + n).Concat(_removed.Select(n => "-" + n)).Take(12));
}
