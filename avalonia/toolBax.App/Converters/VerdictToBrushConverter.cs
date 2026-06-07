using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FoToolbox.Core.DualWrite;

namespace ToolBax.App.Converters;

/// <summary>
/// Maps a <see cref="DualWriteComparisonVerdict"/> to a themed status brush (ok / warn / info / err) so
/// the compare chips and diff badge are colour-coded without per-verdict XAML branching.
/// </summary>
public sealed class VerdictToBrushConverter : IValueConverter
{
    public static readonly VerdictToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is DualWriteComparisonVerdict verdict
            ? verdict switch
            {
                DualWriteComparisonVerdict.Identical => "OkBrush",
                DualWriteComparisonVerdict.VersionMismatch or DualWriteComparisonVerdict.StateMismatch => "WarnBrush",
                DualWriteComparisonVerdict.OnlyInRight => "InfoBrush",
                DualWriteComparisonVerdict.OnlyInLeft => "ErrBrush",
                _ => "Text2Brush",
            }
            : "Text2Brush";

        if (Application.Current?.TryGetResource(key, null, out var brush) == true && brush is IBrush resolved)
        {
            return resolved;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
