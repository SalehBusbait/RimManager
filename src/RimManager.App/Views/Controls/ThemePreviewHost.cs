using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace RimManager.App.Views.Controls;

/// <summary>
/// Hosts a theme-gallery card's mini-preview under THAT theme's own tokens
/// (T4, S-GALLERY): merging the target dictionary into this control's Resources
/// makes every <c>DynamicResource Rm*</c> inside resolve locally — the card
/// previews Ice with Ice's values while the app runs Toxic, and nothing ever
/// writes the application-level theme until the user picks (implementation-notes:
/// "by scoping, never by writing").
/// </summary>
public sealed class ThemePreviewHost : ContentControl
{
    public static readonly StyledProperty<string?> ThemeIdProperty =
        AvaloniaProperty.Register<ThemePreviewHost, string?>(nameof(ThemeId));

    public string? ThemeId
    {
        get => GetValue(ThemeIdProperty);
        set => SetValue(ThemeIdProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeIdProperty) ApplyDictionary();
    }

    private void ApplyDictionary()
    {
        Resources.MergedDictionaries.Clear();
        if (ThemeId is not { Length: > 0 } id) return;

        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://RimManager/"))
        {
            Source = new Uri($"avares://RimManager/Themes/Tokens.{id}.axaml"),
        });
    }
}
