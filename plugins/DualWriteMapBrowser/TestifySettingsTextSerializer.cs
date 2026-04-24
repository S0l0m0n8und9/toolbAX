using System;
using System.Collections.Generic;
using System.Linq;

namespace DualWriteMapBrowserPlugin;

internal static class TestifySettingsTextSerializer
{
    public static string FormatLines(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        return string.Join(
            "\r\n",
            values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
    }

    public static HashSet<string> ParseLines(string? text)
    {
        var values = SplitLines(text)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim());

        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        return string.Join(
            "\r\n",
            values
                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
                .OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
    }

    public static Dictionary<string, string> ParseKeyValueLines(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(text))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new FormatException("Preferred value entries require a field name before '='.");
            }

            values[key] = value;
        }

        return values;
    }

    private static IEnumerable<string> SplitLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
}
