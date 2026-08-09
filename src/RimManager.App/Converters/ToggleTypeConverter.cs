using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace RimManager.App.Converters;

/// <summary>
/// Maps "is this row checkable" to a <see cref="MenuItemToggleType"/>, so the View
/// menu's toggles get their ✓ slot while ordinary commands do not.
/// <para>
/// Needed because the menu is generated from data rather than authored per-item:
/// the container theme sets one binding for every row, so the distinction has to
/// travel as a value.
/// </para>
/// </summary>
public sealed class ToggleTypeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? MenuItemToggleType.CheckBox : MenuItemToggleType.None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("ToggleTypeConverter is one-way.");
}
