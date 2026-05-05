using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DualWriteMapBrowserPlugin;

public static class TestifyValueMapParser
{
    private static readonly string[] SourceKeyCandidates =
    {
        "source",
        "from",
        "sourceValue",
        "key",
        "name"
    };

    private static readonly string[] TargetKeyCandidates =
    {
        "target",
        "to",
        "destination",
        "destinationValue",
        "targetValue",
        "value"
    };

    public static bool TryExtractKeys(string valueMapJson, out HashSet<string> keys, out string error)
    {
        keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;

        if (!TryExtractMappings(valueMapJson, out var mappings, out error))
        {
            return false;
        }

        foreach (var key in mappings.Keys)
        {
            keys.Add(key);
        }

        return true;
    }

    public static bool TryExtractMappings(string valueMapJson, out Dictionary<string, string?> mappings, out string error)
    {
        mappings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(valueMapJson))
        {
            error = "ValueMap is empty.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(valueMapJson);
            var root = doc.RootElement;
            switch (root.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in root.EnumerateObject())
                    {
                        if (!string.IsNullOrWhiteSpace(property.Name))
                        {
                            mappings[property.Name.Trim()] = GetPrimitiveValue(property.Value);
                        }
                    }

                    if (mappings.Count == 0)
                    {
                        error = "ValueMap object did not contain keys.";
                        return false;
                    }

                    return true;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in root.EnumerateArray())
                    {
                        index++;
                        if (!TryExtractArrayItemMapping(item, out var source, out var target))
                        {
                            error = $"ValueMap array item {index} did not contain a recognizable source key.";
                            return false;
                        }

                        if (!string.IsNullOrWhiteSpace(source))
                        {
                            mappings[source] = target;
                        }
                    }

                    if (mappings.Count == 0)
                    {
                        error = "ValueMap array did not contain keys.";
                        return false;
                    }

                    return true;

                default:
                    error = $"Unsupported valueMap JSON kind: {root.ValueKind}.";
                    return false;
            }
        }
        catch (JsonException ex)
        {
            error = $"Invalid valueMap JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryExtractArrayItemKey(JsonElement item, out string key)
    {
        key = string.Empty;

        if (item.ValueKind == JsonValueKind.String)
        {
            key = item.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(key);
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var candidate in SourceKeyCandidates)
        {
            if (item.TryGetProperty(candidate, out var value) && value.ValueKind == JsonValueKind.String)
            {
                key = value.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(key);
            }
        }

        var firstPrimitive = item.EnumerateObject()
            .FirstOrDefault(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False);

        if (firstPrimitive.Equals(default(JsonProperty)))
        {
            return false;
        }

        key = firstPrimitive.Value.ValueKind == JsonValueKind.String
            ? firstPrimitive.Value.GetString() ?? string.Empty
            : firstPrimitive.Value.ToString();

        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryExtractArrayItemMapping(JsonElement item, out string source, out string? target)
    {
        source = string.Empty;
        target = null;

        if (!TryExtractArrayItemKey(item, out source))
        {
            return false;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        foreach (var candidate in TargetKeyCandidates)
        {
            if (item.TryGetProperty(candidate, out var value))
            {
                target = GetPrimitiveValue(value);
                return true;
            }
        }

        var firstTarget = item.EnumerateObject()
            .FirstOrDefault(p =>
                !SourceKeyCandidates.Contains(p.Name, StringComparer.OrdinalIgnoreCase) &&
                p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null);
        if (!firstTarget.Equals(default(JsonProperty)))
        {
            target = GetPrimitiveValue(firstTarget.Value);
        }

        return true;
    }

    private static string? GetPrimitiveValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }
}
