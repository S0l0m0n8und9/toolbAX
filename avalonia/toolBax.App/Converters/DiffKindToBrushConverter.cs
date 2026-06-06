using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ToolBax.Core.Models;

namespace ToolBax.App.Converters;

/// <summary>
/// Maps a <see cref="DiffKind"/> to a themed status brush (ok / warn / info / err) so compare chips
/// and the diff badge are colour-coded without per-kind XAML branching.
/// </summary>
public sealed class DiffKindToBrushConverter : IValueConverter
{
    public static readonly DiffKindToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is DiffKind kind
            ? kind switch
            {
                DiffKind.InSync => "OkBrush",
                DiffKind.VersionDrift or DiffKind.StateDiffers => "WarnBrush",
                DiffKind.RowDelta or DiffKind.OnlyInTarget => "InfoBrush",
                DiffKind.OnlyInSource => "ErrBrush",
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
