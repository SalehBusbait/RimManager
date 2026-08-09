using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>
/// A Steam Workshop collection resolved to its members, from
/// <c>ISteamRemoteStorage/GetCollectionDetails</c>. <see cref="MemberIds"/> are
/// published-file ids in Steam's declared <c>sortorder</c>; they are the raw import
/// input that gets reconciled against what's installed.
/// </summary>
public sealed record WorkshopCollection
{
    public required string CollectionId { get; init; }

    /// <summary>Whether Steam resolved this collection id (result 1) or not (e.g. result 9).</summary>
    public WorkshopItemResult Result { get; init; }

    /// <summary>Member published-file ids, ordered by Steam's <c>sortorder</c>.</summary>
    public ImmutableArray<string> MemberIds { get; init; } = [];

    public bool IsOk => Result == WorkshopItemResult.Ok;
}
