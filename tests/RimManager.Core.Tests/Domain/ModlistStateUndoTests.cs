using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Undo;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// Demonstrates the intended Phase 4/5 usage: list mutations are modeled as new
/// immutable <see cref="ModlistState"/> snapshots pushed onto an
/// <see cref="UndoHistory{T}"/>, so undo/redo is snapshot restore.
/// </summary>
public sealed class ModlistStateUndoTests
{
    private static ModlistEntry Mod(string id) => new(ModlistEntryKind.Mod, id, id);

    [Fact]
    public void Reorder_is_undoable_via_snapshots()
    {
        var initial = ModlistState.Empty.WithEntries([Mod("a"), Mod("b"), Mod("c")]);
        var history = new UndoHistory<ModlistState>(initial);

        // "Reorder" -> produce a new snapshot with a and b swapped.
        var reordered = initial.WithEntries([Mod("b"), Mod("a"), Mod("c")]);
        history.Push(reordered);

        history.Current.Entries.Select(e => e.Id).Should().Equal("b", "a", "c");

        history.Undo();
        history.Current.Entries.Select(e => e.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Immutable_snapshots_do_not_alias()
    {
        var s1 = ModlistState.Empty.WithEntries([Mod("a")]);
        var s2 = s1.WithEntries([Mod("a"), Mod("b")]);

        s1.Entries.Should().HaveCount(1, "the earlier snapshot must be unaffected by the later one");
        s2.Entries.Should().HaveCount(2);
        ReferenceEquals(s1.Entries, s2.Entries).Should().BeFalse();
    }

    [Fact]
    public void Empty_is_a_singleton_empty_list()
    {
        ModlistState.Empty.Entries.Should().BeEmpty();
        ModlistState.Empty.Entries.Should().BeSameAs(ImmutableList<ModlistEntry>.Empty);
    }
}
