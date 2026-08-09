using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using RimManager.App.ViewModels;

namespace RimManager.App.Themes;

/// <summary>
/// The per-theme app mark (T4, S-BRAND): each theme's badge, rasterised at build
/// time by assets/brand/BadgeRasterizer from the accepted design's literal badge
/// crops, shipped as <c>Assets/marks/mark-&lt;Id&gt;.png</c>. Theme switch swaps
/// the mark everywhere it appears; the .exe/.ico stays Drop Pods Dark by design
/// (a file icon cannot change at runtime).
/// </summary>
public static class ThemeAssets
{
    /// <summary>
    /// Written by the hub's theme application (the single theme writer), so windows
    /// constructed later — About, first-run — pick the current mark without needing
    /// a binding path into the hub.
    /// </summary>
    public static AppTheme CurrentTheme { get; set; } = AppTheme.FollowSystem;

    public static Bitmap? Mark(AppTheme theme)
    {
        try
        {
            using var stream = AssetLoader.Open(MarkUri(theme));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            // Headless (tests) or a missing asset: a null mark renders nothing,
            // which is better than a construction-time crash in About.
            return null;
        }
    }

    public static Bitmap? CurrentMark() => Mark(CurrentTheme);

    public static WindowIcon? Icon(AppTheme theme)
    {
        try
        {
            using var stream = AssetLoader.Open(MarkUri(theme));
            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Uri MarkUri(AppTheme theme) =>
        new($"avares://RimManager/Assets/marks/mark-{Resolve(theme)}.png");

    /// <summary>Follow-system resolves to the Drop Pods pair by the OS's side —
    /// the same pairing the theme dictionaries use.</summary>
    private static AppTheme Resolve(AppTheme theme) =>
        theme != AppTheme.FollowSystem
            ? theme
            : Application.Current?.ActualThemeVariant == ThemeVariant.Light
                ? AppTheme.DropPodsLight
                : AppTheme.DropPodsDark;
}
