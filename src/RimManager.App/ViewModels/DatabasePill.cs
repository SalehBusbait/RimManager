namespace RimManager.App.ViewModels;

/// <summary>
/// The state pill on a community-database card (T5/S-INTEG): one word the status
/// sentence then elaborates. Four states — <c>active</c> (on, has data),
/// <c>not synced</c> (on, empty: wants something, so it warns), <c>sync failed</c>
/// (on, and the last sync ERRORED — bad, with the message riding the tooltip so the
/// alarm carries its sentence), and <c>off</c> (neutral: a choice, not a problem).
/// <para>
/// Off beats error: a database not in use has no news. Error beats active: cached
/// data may still be serving, which the status line beneath says — but "the upstream
/// broke" is the headline. CONNECTIVITY failures never reach here (the offline
/// system owns those); only a server or parse failure is the card's own news.
/// </para>
/// </summary>
public readonly record struct DatabasePill(
    string Text, bool IsOn, bool IsWarn, bool IsBad = false, string? Tip = null)
{
    public static DatabasePill For(bool enabled, int count, string? syncError = null)
    {
        if (!enabled) return new DatabasePill("off", IsOn: false, IsWarn: false);
        if (!string.IsNullOrEmpty(syncError))
        {
            return new DatabasePill("sync failed", IsOn: false, IsWarn: false,
                IsBad: true, Tip: syncError);
        }

        return count > 0
            ? new DatabasePill("active", IsOn: true, IsWarn: false)
            : new DatabasePill("not synced", IsOn: false, IsWarn: true);
    }
}
