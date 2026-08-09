using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

public enum OrderDiffRowKind { Insert, Remove, Move }

/// <summary>One row of the review table: a mark, a name, and the mono position text.</summary>
public sealed record OrderDiffRow(
    OrderDiffRowKind Kind, string Name, string PositionText)
{
    public bool IsInsert => Kind == OrderDiffRowKind.Insert;
    public bool IsRemove => Kind == OrderDiffRowKind.Remove;
    public bool IsMove => Kind == OrderDiffRowKind.Move;
}

/// <summary>
/// The order-diff dialog's Avalonia-free decisions (S-ORDERDIFF): the honest headline
/// and the row table. Kept beside <see cref="GameMovedNotice"/>, whose strip opens it.
/// </summary>
public static class OrderDiffPresenter
{
    /// <summary>
    /// "1 insert · 2 moves — 544 rows unchanged". Zero-count parts are omitted —
    /// a headline enumerating "0 inserts" pads the sentence with nothing.
    /// </summary>
    public static string Headline(OrderDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        if (diff.IsIdentical)
            return $"No differences — {diff.UnchangedCount} rows identical";

        var parts = new List<string>(3);
        if (!diff.Inserted.IsDefaultOrEmpty)
            parts.Add(diff.Inserted.Length == 1 ? "1 insert" : $"{diff.Inserted.Length} inserts");
        if (!diff.Removed.IsDefaultOrEmpty)
            parts.Add(diff.Removed.Length == 1 ? "1 removal" : $"{diff.Removed.Length} removals");
        if (!diff.Moved.IsDefaultOrEmpty)
            parts.Add(diff.Moved.Length == 1 ? "1 move" : $"{diff.Moved.Length} moves");

        var rows = diff.UnchangedCount == 1 ? "1 row unchanged" : $"{diff.UnchangedCount} rows unchanged";
        return $"{string.Join(" · ", parts)} — {rows}";
    }

    /// <summary>
    /// Inserts and moves in the INCOMING order (that is the order being judged),
    /// removals at the end in the order they held on screen — a dropped row has no
    /// position in "theirs" to sort by.
    /// </summary>
    public static ImmutableArray<OrderDiffRow> Rows(OrderDiff diff, Func<ModId, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(nameOf);

        string Name(ModId id) => nameOf(id) is { Length: > 0 } name ? name : id.Display;

        var incoming = new List<(int TheirsPosition, OrderDiffRow Row)>();
        foreach (var insert in diff.Inserted)
        {
            incoming.Add((insert.TheirsPosition, new OrderDiffRow(
                OrderDiffRowKind.Insert, Name(insert.Id),
                $"theirs #{insert.TheirsPosition}")));
        }
        foreach (var move in diff.Moved)
        {
            incoming.Add((move.TheirsPosition, new OrderDiffRow(
                OrderDiffRowKind.Move, Name(move.Id),
                $"yours #{move.YoursPosition} → theirs #{move.TheirsPosition}")));
        }

        var rows = ImmutableArray.CreateBuilder<OrderDiffRow>();
        rows.AddRange(incoming.OrderBy(r => r.TheirsPosition).Select(r => r.Row));
        rows.AddRange(diff.Removed.Select(remove => new OrderDiffRow(
            OrderDiffRowKind.Remove, Name(remove.Id), $"yours #{remove.YoursPosition}")));
        return rows.ToImmutable();
    }
}

/// <summary>
/// Backs the S-ORDERDIFF task dialog. Modal in the confirm family: one decision, and
/// closing by any route other than "Take theirs" leaves <see cref="Accepted"/> false.
/// </summary>
public sealed class OrderDiffViewModel(OrderDiff diff, Func<ModId, string?> nameOf)
{
    public string Headline { get; } = OrderDiffPresenter.Headline(diff);

    public ImmutableArray<OrderDiffRow> Rows { get; } = OrderDiffPresenter.Rows(diff, nameOf);

    public const string FooterSentence = "Either way, a snapshot is taken first.";

    /// <summary>Set only by the "Take theirs" button; the view model never sets it.</summary>
    public bool Accepted { get; set; }
}
