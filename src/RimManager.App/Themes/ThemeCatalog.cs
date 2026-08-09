using System.Collections.Immutable;
using Avalonia.Styling;
using RimManager.App.ViewModels;

namespace RimManager.App.Themes;

/// <summary>One theme's identity: the enum member, display name, and variant.</summary>
public sealed record ThemeInfo(AppTheme Theme, string DisplayName, bool IsLight, ThemeVariant Variant);

/// <summary>
/// The theme roster (design handoff v2, README §roster) — the one place the ten
/// themes are enumerated, so the Settings list, the T4 gallery, ApplyTheme and the
/// tests all read the same set.
/// <para>
/// The Drop Pods pair rides Avalonia's built-in <see cref="ThemeVariant.Light"/> /
/// <see cref="ThemeVariant.Dark"/> keys — which is what makes "follow system"
/// (<see cref="ThemeVariant.Default"/>) resolve to the pair with no code: the OS
/// picks the side, Avalonia picks the dictionary. The eight flavoured themes are
/// CUSTOM variants, each inheriting Dark so un-themed Fluent internals fall back
/// sanely; each is a destination, not a mode (the handoff's follow-system
/// decision).
/// </para>
/// </summary>
public static class ThemeCatalog
{
    // Public static fields, not properties: App.axaml keys its ThemeDictionaries
    // on these via {x:Static}, which binds to fields.
    public static readonly ThemeVariant TribalVariant = new("RmTribal", ThemeVariant.Dark);
    public static readonly ThemeVariant AridVariant = new("RmArid", ThemeVariant.Light);
    public static readonly ThemeVariant IceVariant = new("RmIce", ThemeVariant.Dark);
    public static readonly ThemeVariant ToxicVariant = new("RmToxic", ThemeVariant.Dark);
    public static readonly ThemeVariant MechVariant = new("RmMech", ThemeVariant.Dark);
    public static readonly ThemeVariant RoyaltyVariant = new("RmRoyalty", ThemeVariant.Dark);
    public static readonly ThemeVariant AnomalyVariant = new("RmAnomaly", ThemeVariant.Dark);
    public static readonly ThemeVariant GlitterVariant = new("RmGlitter", ThemeVariant.Dark);

    /// <summary>The ten themes, roster order. Follow-system is a mode, not a member.</summary>
    public static readonly ImmutableArray<ThemeInfo> All =
    [
        new(AppTheme.DropPodsDark, "Drop Pods Dark", IsLight: false, ThemeVariant.Dark),
        new(AppTheme.DropPodsLight, "Drop Pods Light", IsLight: true, ThemeVariant.Light),
        new(AppTheme.Tribal, "Tribal Dawn", IsLight: false, TribalVariant),
        new(AppTheme.Arid, "Arid Rim", IsLight: true, AridVariant),
        new(AppTheme.Ice, "Ice Sheet", IsLight: false, IceVariant),
        new(AppTheme.Toxic, "Toxic Fallout", IsLight: false, ToxicVariant),
        new(AppTheme.Mech, "Mechanoid Threat", IsLight: false, MechVariant),
        new(AppTheme.Royalty, "Imperial Court", IsLight: false, RoyaltyVariant),
        new(AppTheme.Anomaly, "Void Provocation", IsLight: false, AnomalyVariant),
        new(AppTheme.Glitter, "Glitterworld", IsLight: false, GlitterVariant),
    ];

    /// <summary>The variant a theme renders with. Follow-system defers to the OS.</summary>
    public static ThemeVariant VariantOf(AppTheme theme)
    {
        if (theme == AppTheme.FollowSystem) return ThemeVariant.Default;
        foreach (var info in All)
            if (info.Theme == theme) return info.Variant;
        return ThemeVariant.Default;
    }

    /// <summary>
    /// The stored name → theme, with the legacy pre-v2 names mapped rather than
    /// reset: an install that chose Light keeps a light theme.
    /// </summary>
    public static AppTheme Parse(string? stored) => stored switch
    {
        "Light" => AppTheme.DropPodsLight,
        "Dark" => AppTheme.DropPodsDark,
        _ => Enum.TryParse<AppTheme>(stored, out var theme) ? theme : AppTheme.FollowSystem,
    };
}
