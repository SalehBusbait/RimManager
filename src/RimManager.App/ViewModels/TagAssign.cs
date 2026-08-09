namespace RimManager.App.ViewModels;

/// <summary>How much of the current selection carries a tag.</summary>
public enum TagAssignState
{
    /// <summary>No selected mod has it.</summary>
    None,

    /// <summary>Some do, some do not — the "1 of 3" case.</summary>
    Some,

    /// <summary>Every selected mod has it.</summary>
    All,
}

/// <summary>
/// The bulk-tagging rules (O7, O8). Pure, because the one rule that matters here is
/// easy to state and easy to get wrong.
/// </summary>
public static class TagAssign
{
    public static TagAssignState StateOf(int assigned, int total)
    {
        if (total <= 0 || assigned <= 0) return TagAssignState.None;
        return assigned >= total ? TagAssignState.All : TagAssignState.Some;
    }

    /// <summary>
    /// Whether clicking a row ADDS the tag to everything selected, or removes it.
    /// <para>
    /// The rule the tri-state exists for: a partial tag ASSIGNS. Clicking "−" means
    /// "give this to all of them", never "take it off the one that has it" — bulk
    /// tagging must not silently clear a tag some rows already carry, because the
    /// row that loses it is off screen and there is nothing to notice.
    /// </para>
    /// </summary>
    public static bool AssignsOnClick(TagAssignState state) => state != TagAssignState.All;

    /// <summary>
    /// What the flyout says it is about to affect. Naming the count is the whole
    /// safety story for bulk assignment — the flyout opens from ONE mod's info pane,
    /// so without it there is nothing on screen to say the click will touch twelve.
    /// </summary>
    public static string Heading(int selectionCount) =>
        selectionCount > 1 ? $"ASSIGN TO {selectionCount} MODS" : "ASSIGN A TAG";

    /// <summary>The result line: "Tagged 12 mods 'Furniture'."</summary>
    public static string Result(bool assigned, int count, string tagName)
    {
        var what = count == 1 ? "1 mod" : $"{count} mods";
        return assigned
            ? $"Tagged {what} “{tagName}”."
            : $"Removed “{tagName}” from {what}.";
    }
}
