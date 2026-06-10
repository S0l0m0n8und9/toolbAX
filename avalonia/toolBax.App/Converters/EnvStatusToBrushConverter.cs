using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ToolBax.Core.Models;

namespace ToolBax.App.Converters;

/// <summary>
/// Maps an <see cref="EnvStatus"/> to a themed status brush for the Profiles master-list status dot:
/// connected → ok (green), token-expired → warn (amber), disconnected → err (red). Mirrors the design
/// prototype's per-row status colour without per-status XAML branching.
/// </summary>
public sealed class EnvStatusToBrushConverter : IValueConverter
{
    public static readonly EnvStatusToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is EnvStatus status
            ? status switch
            {
                EnvStatus.Connected => "OkBrush",
                EnvStatus.TokenExpired => "WarnBrush",
                _ => "ErrBrush",
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
