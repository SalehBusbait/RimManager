using System.Collections.Immutable;

namespace RimManager.Core.Sharing;

/// <summary>
/// Which Workshop list items have already had their one import offer (NF-10:
/// "offered once per item" — the strip and its dialog never nag; the row's context
/// menu is the standing re-offer). Keyed by the item's Workshop id, falling back to
/// packageId for the odd item without one. Persisted per install by
/// <c>WorkspaceStateRepository</c>, like the snoozes it resembles.
/// </summary>
public sealed record RwListOfferSeen
{
    public static RwListOfferSeen Empty { get; } = new();

    public ImmutableArray<string> SeenItems { get; init; } = [];

    public bool Contains(string key) =>
        SeenItems.Contains(key, StringComparer.OrdinalIgnoreCase);

    public RwListOfferSeen MarkSeen(string key) =>
        Contains(key) ? this : this with { SeenItems = SeenItems.Add(key) };
}
