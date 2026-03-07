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

    public static bool TryExtractKeys(string valueMapJson, out HashSet<string> keys, out string error)
    {
        keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                            keys.Add(property.Name.Trim());
                        }
                    }

                    if (keys.Count == 0)
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
                        if (!TryExtractArrayItemKey(item, out var key))
                        {
                            error = $"ValueMap array item {index} did not contain a recognizable source key.";
                            return false;
                        }

                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            keys.Add(key);
                        }
                    }

                    if (keys.Count == 0)
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
}