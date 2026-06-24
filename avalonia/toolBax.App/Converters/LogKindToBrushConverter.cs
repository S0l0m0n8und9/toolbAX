using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Converters;

/// <summary>
/// Maps a <see cref="LogKind"/> to a themed brush so each gateway-log line is colour-coded
/// (info / ok / warn / err) without per-line XAML branching.
/// </summary>
public sealed class LogKindToBrushConverter : IValueConverter
{
    public static readonly LogKindToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is LogKind kind
            ? kind switch
            {
                LogKind.Ok => "OkBrush",
                LogKind.Warn => "WarnBrush",
                LogKind.Err => "ErrBrush",
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
