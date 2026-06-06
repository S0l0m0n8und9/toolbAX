using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ToolBax.Core.Models;

namespace ToolBax.App.Converters;

/// <summary>Renders a <see cref="DiAuthMode"/> as its friendly label in the DI mode dropdown.</summary>
public sealed class DiAuthModeLabelConverter : IValueConverter
{
    public static readonly DiAuthModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DiAuthMode mode ? mode.Label() : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
