namespace RimManager.App.ViewModels;

/// <summary>
/// How a row's NAME cell is divided between the mod name and the tag-pill zone
/// (v2 §4A.1). Pure, so the negotiation is testable without rendering a row.
/// <para>
/// It exists because no stock panel expresses the rule. A Grid <c>*,Auto</c>
/// measures the Auto child at INFINITE width, so the pills label everything and
/// paint over the packageId column; a fixed <c>MaxWidth</c> on the zone is blind to
/// the row, so a short name on a wide window collapsed to dots with half the row
/// empty; and a DockPanel measures the docked child before the one it has to leave
/// room for, so the reserve can only ever be a constant. Both children have to be
/// measured before either is allocated, which is what <see cref="NamePillPanel"/>
/// does with this.
/// </para>
/// </summary>
public static class NamePillSplit
{
    /// <summary>
    /// Divides <paramref name="available"/> between a name wanting
    /// <paramref name="nameWant"/> and a pill zone wanting <paramref name="pillWant"/>.
    /// <para>
    /// The name is served first, but never more than it asks for — that is what frees
    /// the leftover space for pills on a short name. When both want more than there
    /// is, the name keeps half the cell, falling back to <paramref name="reserve"/>
    /// once half is less than that: a floor for narrow windows, a fair share for wide
    /// ones. The pill zone takes what remains, capped at what it wants, and its own
    /// degradation ladder spends whatever it is given.
    /// </para>
    /// </summary>
    public static (double Name, double Pills) Split(
        double available, double nameWant, double pillWant, double reserve)
    {
        if (available <= 0) return (0, 0);
        if (pillWant <= 0) return (available, 0);

        var floor = Math.Max(reserve, available / 2);
        var nameKeeps = Math.Min(nameWant, floor);
        var pills = Math.Clamp(available - nameKeeps, 0, pillWant);

        return (Math.Max(0, available - pills), pills);
    }
}
