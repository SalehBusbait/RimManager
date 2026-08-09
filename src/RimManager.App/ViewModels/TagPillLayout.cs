namespace RimManager.App.ViewModels;

/// <summary>
/// The tag-pill zone's degradation ladder (v2 §4A.1): labelled pills while they
/// fit, then colour dots, then a "+n" overflow — every tag always represented.
/// Pure, so the ladder is testable without rendering text.
/// </summary>
public static class TagPillLayout
{
    /// <summary>
    /// How the zone spends its budget. <paramref name="pillWidths"/> are the
    /// measured labelled-pill widths in order; <paramref name="gap"/> separates
    /// every drawn element; <paramref name="dotWidth"/> is a colour dot;
    /// <paramref name="overflowWidth"/> is the "+n" text for however many remain.
    /// </summary>
    public static (int Labelled, int Dots, int Overflow) Arrange(
        IReadOnlyList<double> pillWidths, double budget, double gap,
        double dotWidth, Func<int, double> overflowWidth)
    {
        ArgumentNullException.ThrowIfNull(pillWidths);
        ArgumentNullException.ThrowIfNull(overflowWidth);

        var n = pillWidths.Count;
        if (n == 0 || budget <= 0) return (0, 0, n);

        // Greedy labelled pills: each must fit alongside what the REST still costs
        // at its cheapest (dots or an overflow), so taking a label can never push
        // the remainder out of representation entirely.
        var used = 0d;
        var labelled = 0;
        for (var i = 0; i < n; i++)
        {
            var lead = labelled > 0 ? gap : 0;
            var restAfter = n - (labelled + 1);
            var restCost = restAfter == 0 ? 0 : gap + Math.Min(dotWidth, overflowWidth(restAfter));
            if (used + lead + pillWidths[i] + restCost > budget) break;

            used += lead + pillWidths[i];
            labelled++;
        }

        // Dots for the rest, same rule against the overflow text.
        var dots = 0;
        var remaining = n - labelled;
        while (remaining - dots > 0)
        {
            var lead = labelled + dots > 0 ? gap : 0;
            var restAfter = remaining - (dots + 1);
            var restCost = restAfter == 0 ? 0 : gap + overflowWidth(restAfter);
            if (used + lead + dotWidth + restCost > budget) break;

            used += lead + dotWidth;
            dots++;
        }

        return (labelled, dots, remaining - dots);
    }
}
