using System.Collections.Immutable;

namespace RimManager.App.ViewModels;

public enum DiffKind
{
    Context,
    Removed,
    Added,

    /// <summary>A collapsed run of identical lines: "14 unchanged lines" (<c>3c</c>).</summary>
    Collapsed,
}

/// <summary>
/// One rendered line of a diff. <paramref name="Number"/> is the line's number in its
/// own side, blank where the side has no line there.
/// </summary>
public sealed record DiffRow(DiffKind Kind, string Number, string Text)
{
    public bool IsAdded => Kind == DiffKind.Added;
    public bool IsRemoved => Kind == DiffKind.Removed;
    public bool IsCollapsed => Kind == DiffKind.Collapsed;
}

/// <summary>Both sides of a two-up diff, plus the counts the footer states.</summary>
public sealed record XmlDiffResult(
    ImmutableArray<DiffRow> Left,
    ImmutableArray<DiffRow> Right,
    int Added,
    int Removed,
    int Collapsed)
{
    public static readonly XmlDiffResult Empty = new([], [], 0, 0, 0);

    /// <summary>"+4 −2 · 14 unchanged collapsed" (<c>3c</c>'s footer).</summary>
    public string Summary =>
        Collapsed > 0
            ? $"+{Added} −{Removed} · {Collapsed} unchanged collapsed"
            : $"+{Added} −{Removed}";

    /// <summary>
    /// The two sides merged into one column, for the Conflicts detail panel's compact
    /// "what changes" block.
    /// <para>
    /// The two-up view has room to show a removal beside its replacement; a 452px
    /// panel does not, and rendering only the right side loses removals entirely — a
    /// change that deletes a line would render as no change at all while the footer
    /// counted it.
    /// </para>
    /// </summary>
    public ImmutableArray<DiffRow> Unified
    {
        get
        {
            var rows = ImmutableArray.CreateBuilder<DiffRow>();
            for (var i = 0; i < Math.Max(Left.Length, Right.Length); i++)
            {
                var left = i < Left.Length ? Left[i] : null;
                var right = i < Right.Length ? Right[i] : null;

                // Removal first, then its replacement: the reading order is
                // "this became that".
                if (left is { Kind: DiffKind.Removed }) rows.Add(left);
                if (right is { Kind: DiffKind.Added }) rows.Add(right);
                if (left is { Kind: DiffKind.Collapsed }) rows.Add(left);
                if (left is { Kind: DiffKind.Context, Text.Length: > 0 }) rows.Add(left);
            }

            return rows.ToImmutable();
        }
    }
}

/// <summary>
/// A line-oriented diff of two XML fragments, for the Conflicts detail panel's
/// "what changes" block and the two-up viewer (<c>3c</c>).
/// <para>
/// Deliberately line-based rather than tree-aware. The question the screen answers is
/// "what did this mod change about the contested element", and the user reads the
/// answer as XML text; a semantic tree diff would be more correct and less legible,
/// and would disagree with the file they open in an editor afterwards.
/// </para>
/// <para>
/// Pure, so the alignment is testable — the failure mode here is a diff that looks
/// plausible and pairs the wrong lines, which no screenshot would catch.
/// </para>
/// </summary>
public static class XmlDiff
{
    /// <summary>Runs of identical lines longer than this collapse to one marker row.</summary>
    public const int CollapseThreshold = 6;

    /// <summary>Context lines kept either side of a collapsed run.</summary>
    private const int ContextLines = 2;

    public static XmlDiffResult Compare(string? left, string? right, bool changedOnly = false)
    {
        var a = Split(left);
        var b = Split(right);
        if (a.Length == 0 && b.Length == 0) return XmlDiffResult.Empty;

        var common = LongestCommonSubsequence(a, b);

        var leftRows = ImmutableArray.CreateBuilder<DiffRow>();
        var rightRows = ImmutableArray.CreateBuilder<DiffRow>();
        int i = 0, j = 0, added = 0, removed = 0;

        foreach (var (ai, bi) in common)
        {
            // Everything before the next matched pair differs. Removals and additions
            // are emitted side by side so the two panes stay vertically aligned —
            // that alignment is the whole point of a two-up view.
            while (i < ai || j < bi)
            {
                var hasLeft = i < ai;
                var hasRight = j < bi;

                if (hasLeft)
                {
                    leftRows.Add(new DiffRow(DiffKind.Removed, (i + 1).ToString(), a[i]));
                    removed++;
                    i++;
                }
                else
                {
                    leftRows.Add(new DiffRow(DiffKind.Context, string.Empty, string.Empty));
                }

                if (hasRight)
                {
                    rightRows.Add(new DiffRow(DiffKind.Added, (j + 1).ToString(), b[j]));
                    added++;
                    j++;
                }
                else
                {
                    rightRows.Add(new DiffRow(DiffKind.Context, string.Empty, string.Empty));
                }
            }

            leftRows.Add(new DiffRow(DiffKind.Context, (i + 1).ToString(), a[i]));
            rightRows.Add(new DiffRow(DiffKind.Context, (j + 1).ToString(), b[j]));
            i++;
            j++;
        }

        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length)
            {
                leftRows.Add(new DiffRow(DiffKind.Removed, (i + 1).ToString(), a[i]));
                removed++;
                i++;
            }
            else
            {
                leftRows.Add(new DiffRow(DiffKind.Context, string.Empty, string.Empty));
            }

            if (j < b.Length)
            {
                rightRows.Add(new DiffRow(DiffKind.Added, (j + 1).ToString(), b[j]));
                added++;
                j++;
            }
            else
            {
                rightRows.Add(new DiffRow(DiffKind.Context, string.Empty, string.Empty));
            }
        }

        var (l, r, collapsed) = changedOnly
            ? Collapse(leftRows.ToImmutable(), rightRows.ToImmutable())
            : (leftRows.ToImmutable(), rightRows.ToImmutable(), 0);

        return new XmlDiffResult(l, r, added, removed, collapsed);
    }

    /// <summary>
    /// Replaces long runs where BOTH sides are unchanged with a single marker, keeping
    /// a couple of context lines. Collapsing is driven by both sides at once so the
    /// panes cannot drift out of alignment.
    /// </summary>
    private static (ImmutableArray<DiffRow>, ImmutableArray<DiffRow>, int) Collapse(
        ImmutableArray<DiffRow> left, ImmutableArray<DiffRow> right)
    {
        var l = ImmutableArray.CreateBuilder<DiffRow>();
        var r = ImmutableArray.CreateBuilder<DiffRow>();
        var collapsedTotal = 0;

        var index = 0;
        while (index < left.Length)
        {
            var run = 0;
            while (index + run < left.Length
                   && left[index + run].Kind == DiffKind.Context
                   && right[index + run].Kind == DiffKind.Context)
            {
                run++;
            }

            if (run > CollapseThreshold)
            {
                for (var k = 0; k < ContextLines; k++) { l.Add(left[index + k]); r.Add(right[index + k]); }

                var hidden = run - (ContextLines * 2);
                collapsedTotal += hidden;
                var marker = new DiffRow(DiffKind.Collapsed, "··", $"{hidden} unchanged lines");
                l.Add(marker);
                r.Add(marker);

                for (var k = run - ContextLines; k < run; k++) { l.Add(left[index + k]); r.Add(right[index + k]); }
                index += run;
                continue;
            }

            if (run > 0)
            {
                for (var k = 0; k < run; k++) { l.Add(left[index + k]); r.Add(right[index + k]); }
                index += run;
                continue;
            }

            l.Add(left[index]);
            r.Add(right[index]);
            index++;
        }

        return (l.ToImmutable(), r.ToImmutable(), collapsedTotal);
    }

    private static string[] Split(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// Indices of a longest common subsequence, as (left, right) pairs. Classic
    /// quadratic LCS — a contested element is tens of lines, never thousands, because
    /// the analyzer retains the element rather than the file.
    /// </summary>
    private static List<(int, int)> LongestCommonSubsequence(string[] a, string[] b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == b[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { pairs.Add((x, y)); x++; y++; }
            else if (table[x + 1, y] >= table[x, y + 1]) x++;
            else y++;
        }

        return pairs;
    }
}
