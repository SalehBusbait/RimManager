using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Workshop;

namespace RimManager.App.ViewModels;

/// <summary>
/// Avalonia-free presentation logic for the Collection panel: orders members
/// (actionable first) and summarizes a <see cref="CollectionReport"/>. The resolve +
/// reconcile is done by Core (<see cref="CollectionReconciler"/>); this only presents it.
/// </summary>
public static class CollectionPresenter
{
    /// <summary>Actionable members first: missing, then delisted, then already-installed;
    /// original collection order preserved within each group.</summary>
    public static ImmutableArray<CollectionMember> Order(IEnumerable<CollectionMember> members) =>
        [.. members.OrderBy(Priority)];

    private static int Priority(CollectionMember m) =>
        m.IsInstalled ? 2 : m.IsDelisted ? 1 : 0;

    /// <summary>
    /// The four-way reconcile in 2e's header: present, to download, unavailable,
    /// already active. Four numbers rather than one sentence because the user is about
    /// to press a button whose scope is exactly one of them.
    /// </summary>
    public static (int Present, int ToDownload, int Unavailable, int AlreadyActive) Reconcile(
        IEnumerable<CollectionMemberRowViewModel> rows)
    {
        var all = rows.ToList();
        return (
            all.Count(r => r.State == MemberState.Present),
            all.Count(r => r.State == MemberState.ToDownload),
            all.Count(r => r.State == MemberState.Unavailable),
            all.Count(r => r.State == MemberState.AlreadyActive));
    }

}
