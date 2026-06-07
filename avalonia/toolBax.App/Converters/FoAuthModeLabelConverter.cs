using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ToolBax.Core.Models;

namespace ToolBax.App.Converters;

/// <summary>
/// Maps a <see cref="FoAuthMode"/> to its friendly label (e.g. "Interactive (MFA)", "Client secret")
/// so the FO / Dataverse auth-mode dropdowns read clearly instead of showing the raw enum name.
/// </summary>
public sealed class FoAuthModeLabelConverter : IValueConverter
{
    public static readonly FoAuthModeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FoAuthMode mode ? mode.Label() : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
