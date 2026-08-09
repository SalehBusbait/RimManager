using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimManager.App.Converters;

/// <summary>
/// True when the bound value equals the converter parameter, compared as text.
/// <para>
/// Its job is to drive <c>Classes.p0="{Binding PaletteIndex, ...}"</c> style-class
/// bindings, which take a bool. Note what it deliberately does <em>not</em> do:
/// return a brush. Resolving <c>RmPalette{n}Brush</c> in a converter would hand back
/// a brush frozen at conversion time, which then survives a theme switch and paints
/// the dark hue on a light window. Returning a bool keeps the DynamicResource in the
/// style, where it re-resolves for free.
/// </para>
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(
            value?.ToString(),
            parameter?.ToString(),
            StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("EqualsConverter is one-way.");
}
