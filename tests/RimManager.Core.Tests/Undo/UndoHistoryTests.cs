using FluentAssertions;
using RimManager.Core.Undo;
using Xunit;

namespace RimManager.Core.Tests.Undo;

public sealed class UndoHistoryTests
{
    [Fact]
    public void Starts_with_initial_state_and_no_history()
    {
        var h = new UndoHistory<string>("a");

        h.Current.Should().Be("a");
        h.CanUndo.Should().BeFalse();
        h.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Push_then_undo_restores_previous_snapshot()
    {
        var h = new UndoHistory<string>("a");
        h.Push("b");

        h.Current.Should().Be("b");
        h.CanUndo.Should().BeTrue();

        h.Undo().Should().Be("a");
        h.Current.Should().Be("a");
        h.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void Redo_moves_forward_again()
    {
        var h = new UndoHistory<string>("a");
        h.Push("b");
        h.Undo();

        h.Redo().Should().Be("b");
        h.Current.Should().Be("b");
        h.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Pushing_after_undo_discards_the_redo_branch()
    {
        var h = new UndoHistory<string>("a");
        h.Push("b");
        h.Push("c");
        h.Undo();            // back to "b"
        h.Push("d");         // new branch — "c" is gone

        h.Current.Should().Be("d");
        h.CanRedo.Should().BeFalse();
        h.Undo().Should().Be("b");
    }

    [Fact]
    public void Capacity_drops_oldest_states()
    {
        var h = new UndoHistory<int>(0, capacity: 3);
        h.Push(1);
        h.Push(2);   // states: [0,1,2], full
        h.Push(3);   // drops 0 -> [1,2,3]

        h.Current.Should().Be(3);
        h.Undo().Should().Be(2);
        h.Undo().Should().Be(1);
        h.CanUndo.Should().BeFalse("the oldest state (0) was evicted by capacity");
    }

    [Fact]
    public void Undo_past_start_throws()
    {
        var h = new UndoHistory<string>("a");
        var act = () => h.Undo();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Capacity_below_one_is_rejected()
    {
        var act = () => new UndoHistory<string>("a", capacity: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
