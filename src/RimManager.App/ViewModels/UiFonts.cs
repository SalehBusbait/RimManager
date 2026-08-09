using System.Collections.Immutable;

namespace RimManager.App.ViewModels;

/// <summary>One entry in the Font dropdown (<c>2g</c>).</summary>
/// <param name="Label">What the dropdown shows.</param>
/// <param name="Family">
/// An Avalonia font-family string. Comma-separated fallbacks matter: naming a family the
/// machine does not have silently renders in whatever the platform substitutes, and on
/// Linux that is often a serif face that breaks every column measurement.
/// </param>
public sealed record UiFont(string Label, string Family);

/// <summary>
/// The UI font choices. Short on purpose: the row templates are built on fixed column
/// widths, so an arbitrary font picker would let a user break every list on the screen.
/// These three are the ones whose metrics were checked against the 20px row.
/// <para>
/// The <b>mono</b> role is deliberately absent (<c>2g</c>: "mono column font follows the
/// system monospace"). Version and packageId columns align because that font is
/// monospaced; offering to change it would offer to break them.
/// </para>
/// </summary>
public static class UiFonts
{
    public static readonly ImmutableArray<UiFont> Choices =
    [
        // Inter ships with the app (Avalonia.Fonts.Inter), so this one always resolves.
        new("System default (Inter)", "Inter"),
        new("Segoe UI / system sans", "Segoe UI,SF Pro Text,Ubuntu,Cantarell,sans-serif"),
        new("Monospace everywhere", "Cascadia Mono,Consolas,SF Mono,DejaVu Sans Mono,monospace"),
    ];

    public static int Clamp(int index) => index < 0 || index >= Choices.Length ? 0 : index;

    public static UiFont Get(int index) => Choices[Clamp(index)];
}
