namespace RimManager.App.ViewModels;

/// <summary>
/// What the shared source badge needs from whatever it is rendering.
/// <para>
/// It exists so the badge stays ONE template across the two lists and mod info. The
/// alternative was a second copy typed to <see cref="ModDetailViewModel"/> — six
/// visibility bindings, six geometries and six tint classes, written out twice. The
/// status slot in the same file shows how that ends: two blocks that are identical
/// only until someone edits one of them.
/// </para>
/// <para>
/// Six bools rather than the <c>ModSource</c> itself, because a bound style class is
/// how a tint survives a theme flip — a converter would hand back a brush resolved at
/// conversion time. The same six also pick which icon is visible.
/// </para>
/// </summary>
public interface IModSourceBadge
{
    bool IsCoreSource { get; }
    bool IsDlcSource { get; }
    bool IsWorkshopSource { get; }
    bool IsLocalSource { get; }
    bool IsGitSource { get; }

    /// <summary>
    /// The source in words, for the badge's tooltip. The badge is a wordless 9px icon,
    /// so this is the only place the answer is written out.
    /// </summary>
    string Source { get; }
}
