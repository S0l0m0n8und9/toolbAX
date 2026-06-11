using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ToolBax.App.Converters;

/// <summary>
/// True when a master-list row's environment id equals the active environment id — drives the per-row
/// "active" badge in the Profiles list. Bind two values: the row's <c>Id</c> and the view-model's
/// <c>ActiveId</c>; the badge re-evaluates whenever the active id changes.
/// </summary>
public sealed class EnvIsActiveConverter : IMultiValueConverter
{
    public static readonly EnvIsActiveConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return false;
        }

        var id = values[0] as string;
        var activeId = values[1] as string;
        return !string.IsNullOrEmpty(id) && string.Equals(id, activeId, StringComparison.Ordinal);
    }
}
