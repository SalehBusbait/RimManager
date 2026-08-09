namespace RimManager.Core.Undo;

/// <summary>
/// Snapshot-based undo/redo over an immutable state <typeparamref name="T"/>.
/// </summary>
/// <remarks>
/// <para>
/// We chose snapshot-diff over command objects deliberately: with reorder,
/// drag-into-separator, bulk enable/tag, delete and sort-apply all mutating the
/// list, hand-writing and testing an inverse for each operation is where undo
/// bugs breed. Here undo is simply "restore the previous snapshot", so it is
/// correct by construction. The state is small (a few thousand lightweight
/// entries) and immutable, so structural sharing keeps snapshots cheap.
/// </para>
/// <para>
/// This is the fine-grained, in-session sibling of the persisted per-apply
/// snapshot history (spec §4.2) — same mechanism, different granularity and
/// lifetime. Not thread-safe; drive it from the UI thread / a single owner.
/// </para>
/// </remarks>
public sealed class UndoHistory<T>
{
    private readonly List<T> _states = [];
    private readonly int _capacity;
    private int _cursor = -1;

    /// <param name="initial">The starting state (history position 0).</param>
    /// <param name="capacity">
    /// Maximum retained states. When exceeded, the oldest is dropped. Must be &gt;= 1.
    /// </param>
    public UndoHistory(T initial, int capacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _states.Add(initial);
        _cursor = 0;
    }

    /// <summary>The state at the current history position.</summary>
    public T Current => _states[_cursor];

    public bool CanUndo => _cursor > 0;

    public bool CanRedo => _cursor < _states.Count - 1;

    /// <summary>
    /// Records <paramref name="next"/> as a new state. Any redo states ahead of
    /// the cursor are discarded (the classic linear-history model).
    /// </summary>
    public void Push(T next)
    {
        // Truncate any redo branch.
        if (_cursor < _states.Count - 1)
        {
            _states.RemoveRange(_cursor + 1, _states.Count - _cursor - 1);
        }

        _states.Add(next);
        _cursor++;

        // Enforce capacity by dropping oldest.
        if (_states.Count > _capacity)
        {
            int overflow = _states.Count - _capacity;
            _states.RemoveRange(0, overflow);
            _cursor -= overflow;
        }
    }

    /// <summary>Moves back one state and returns it. Throws if <see cref="CanUndo"/> is false.</summary>
    public T Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("Nothing to undo.");
        }

        return _states[--_cursor];
    }

    /// <summary>Moves forward one state and returns it. Throws if <see cref="CanRedo"/> is false.</summary>
    public T Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("Nothing to redo.");
        }

        return _states[++_cursor];
    }
}
