using CommunityToolkit.Mvvm.ComponentModel;

namespace RimManager.App.ViewModels;

/// <summary>
/// State for the inline Apply bar (<c>2i</c>-2), including its blocking variant.
/// <para>
/// Design non-negotiable #4: Apply is an inline 44px bar, never a modal. It has to
/// carry enough to make that safe — what will be written, the diff summary and the
/// backup filename — because the user is confirming a write to their game folder
/// without a dialog forcing them to stop.
/// </para>
/// </summary>
public sealed partial class CommitBarViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVisible;

    /// <summary>
    /// True when blocking warnings exist. The bar keeps its shape and gains a danger
    /// top border, the reason, and an "Apply anyway" verb — a different surface would
    /// make the user relearn where the answer lives.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    private bool _isBlocked;

    /// <summary>"Write 214 mods to ModsConfig.xml?" or the blocked reason.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>Backup filename plus the diff summary — the reassurance line.</summary>
    [ObservableProperty] private string _detail = string.Empty;

    /// <summary>The primary button's verb. Never "OK".</summary>
    public string ConfirmLabel => IsBlocked ? "Apply anyway" : "Write";

    /// <summary>
    /// Raises the bar, with the reasons it is worth raising for.
    /// <para>
    /// It used to take a fabricated backup filename —
    /// <c>Config/ModsConfig.yyyy-MM-dd-HHmm.bak</c> — which was not the name the writer
    /// produces (<c>ModsConfig.xml.yyyyMMddTHHmmssZ.bak</c>, beside the original), so the
    /// one reassuring detail on the bar named a file that would never exist. The real path
    /// comes back from <c>ApplyResult.BackupPath</c> <em>after</em> the write and is
    /// reported in the status bar, which is where a fact known only afterwards belongs.
    /// </para>
    /// </summary>
    public void Show(int modCount, string reasons)
    {
        IsBlocked = false;
        Title = ApplyConcerns.Title(modCount);
        Detail = reasons;
        IsVisible = true;
    }

    /// <summary>
    /// Raises the blocking variant. Settings ▸ Advanced can turn the refusal off, but
    /// the default is to refuse: a missing dependency means the game fails to load,
    /// and finding that out from RimWorld's own error screen is far worse.
    /// </summary>
    public void ShowBlocked(string reason)
    {
        IsBlocked = true;
        Title = "Cannot apply — blocking warnings";
        Detail = reason;
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}
