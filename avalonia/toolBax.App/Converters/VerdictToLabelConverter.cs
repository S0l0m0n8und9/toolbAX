using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FoToolbox.Core.DualWrite;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Converters;

/// <summary>
/// Maps a <see cref="DualWriteComparisonVerdict"/> to its friendly label (e.g. "version mismatch") via
/// <see cref="CompareVerdict.Label"/>, so the diff grid column matches the summary chips instead of
/// showing the raw enum name.
/// </summary>
public sealed class VerdictToLabelConverter : IValueConverter
{
    public static readonly VerdictToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DualWriteComparisonVerdict verdict ? CompareVerdict.Label(verdict) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
