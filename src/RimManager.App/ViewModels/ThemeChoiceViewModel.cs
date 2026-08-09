using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.App.Themes;

namespace RimManager.App.ViewModels;

/// <summary>
/// One card of the theme gallery on Settings ▸ Appearance (T1 built the shape as a
/// list; T4 made it the gallery, S-GALLERY).
/// <para>
/// The log-level shape on purpose: the choice fires from <see cref="ChooseCommand"/>
/// — a click and nothing else — and <see cref="IsSelected"/> is display state with
/// no side effect, written only by the owner's resync from the one live preference.
/// </para>
/// </summary>
public sealed partial class ThemeChoiceViewModel(AppTheme theme, string label, Action<AppTheme> choose)
    : ObservableObject
{
    public AppTheme Theme { get; } = theme;
    public string Label { get; } = label;

    /// <summary>The enum member IS the asset id: <c>Tokens.&lt;Id&gt;.axaml</c> scopes
    /// the card's mini-preview, <c>mark-&lt;Id&gt;.png</c> is its badge. Empty for
    /// follow-system, whose card is authored separately — it previews a pair, not a
    /// theme.</summary>
    public string Id { get; } = theme == AppTheme.FollowSystem ? "" : theme.ToString();

    /// <summary>The theme's own badge, 15px on the card (S-GALLERY). Null headless.</summary>
    public Avalonia.Media.Imaging.Bitmap? Badge { get; } =
        theme == AppTheme.FollowSystem ? null : ThemeAssets.Mark(theme);

    /// <summary>The bare theme name for the card — <see cref="Label"/> keeps the
    /// "· light" suffix for automation and the follow row.</summary>
    public string DisplayName { get; } =
        ThemeCatalog.All.FirstOrDefault(t => t.Theme == theme)?.DisplayName ?? label;

    /// <summary>"dark" / "light" under the name — the one fact a mini-preview may not
    /// make obvious on a theme like Arid.</summary>
    public string VariantLabel { get; } =
        ThemeCatalog.All.FirstOrDefault(t => t.Theme == theme) is { IsLight: true }
            ? "light" : "dark";

    /// <summary>Display state only — written by the owner's resync, never a trigger.</summary>
    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Choose() => choose(Theme);
}
