namespace RimManager.App.ViewModels;

/// <summary>
/// When the Activity log offers a way back to its newest line (Bug 2).
/// <para>
/// One line, extracted because it is the whole of a behaviour rather than a detail of
/// one: Follow disarming itself on scroll-up is right, and it was silent — lines kept
/// arriving below the viewport and the log looked stopped. Whether to offer the way
/// back is the decision that fixes that, and a decision worth stating once and testing
/// is worth not burying in a view model that needs ten services to construct.
/// </para>
/// </summary>
public static class ActivityJump
{
    /// <summary>
    /// True only when Follow is off AND the newest line is off screen. Following will
    /// arrive there by itself, and being there already leaves nothing to jump to — so
    /// the control appears as an answer to a question the user is already asking,
    /// rather than as one more thing to read past.
    /// </summary>
    public static bool CanJump(bool following, bool atEnd) => !following && !atEnd;
}
