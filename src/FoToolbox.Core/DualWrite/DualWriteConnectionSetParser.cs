using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Parses the <c>GET api/ConnectionSet/{cname}</c> response into a
/// <see cref="DualWriteConnectionSet"/>. Note the MS shape quirks (verified against
/// <c>DWConnectionSet.cs</c>): <c>environments</c> is a JSON object keyed by env name (not an
/// array), and legal entities live at
/// <c>dualWriteDetail.legalEntityMappings.mappings[].left.name</c>. Tolerant/case-insensitive.
/// </summary>
public static class DualWriteConnectionSetParser
{
    public static DualWriteConnectionSet Parse(string json)
    {
        // Same guard as the response parser: a proxy/WAF HTML page on a 2xx must not reach the user as
        // "'<' is an invalid start of a value".
        using var doc = DualWriteResponseParser.ParseGatewayJson(json);
        var root = doc.RootElement;

        var name = GetString(root, "name");
        var environments = ParseEnvironments(root);
        var legalEntities = ParseLegalEntities(root);
        return new DualWriteConnectionSet(name, environments, legalEntities);
    }

    private static IReadOnlyList<DualWriteConnectionSetEnvironment> ParseEnvironments(JsonElement root)
    {
        var result = new List<DualWriteConnectionSetEnvironment>();
        if (!TryGet(root, "environments", out var environments) || environments.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in environments.EnumerateObject())
        {
            var env = property.Value;
            if (env.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(new DualWriteConnectionSetEnvironment(
                Name: GetString(env, "name"),
                DisplayName: GetString(env, "environmentDisplayName", "connectionDisplayName"),
                PowerAppsEnvironment: GetString(env, "powerAppsEnvironment"),
                IsDevInstance: GetBool(env, "isDevInstance"),
                TargetType: GetString(env, "targetType"),
                DirectUrl: GetString(env, "directUrl"),
                Schemas: ParseSchemas(env)));
        }

        return result;
    }

    private static IReadOnlyList<DualWriteSchema> ParseSchemas(JsonElement env)
    {
        var schemas = new List<DualWriteSchema>();
        if (!TryGet(env, "schemas", out var schemasEl) || schemasEl.ValueKind != JsonValueKind.Array)
        {
            return schemas;
        }

        foreach (var schema in schemasEl.EnumerateArray())
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            schemas.Add(new DualWriteSchema(GetString(schema, "name"), ParseKeys(schema)));
        }

        return schemas;
    }

    private static IReadOnlyList<DualWriteSchemaKey> ParseKeys(JsonElement schema)
    {
        var keys = new List<DualWriteSchemaKey>();
        if (!TryGet(schema, "keys", out var keysEl) || keysEl.ValueKind != JsonValueKind.Array)
        {
            return keys;
        }

        foreach (var key in keysEl.EnumerateArray())
        {
            if (key.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            keys.Add(new DualWriteSchemaKey(
                GetString(key, "name"),
                GetStringOrNull(key, "displayName"),
                ParseStringList(key, "fields")));
        }

        return keys;
    }

    private static IReadOnlyList<string> ParseLegalEntities(JsonElement root)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGet(root, "dualWriteDetail", out var detail) ||
            !TryGet(detail, "legalEntityMappings", out var leMappings) ||
            !TryGet(leMappings, "mappings", out var mappings) ||
            mappings.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var mapping in mappings.EnumerateArray())
        {
            if (mapping.ValueKind == JsonValueKind.Object &&
                TryGet(mapping, "left", out var left) &&
                left.ValueKind == JsonValueKind.Object)
            {
                var name = GetString(left, "name");
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                {
                    result.Add(name);
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ParseStringList(JsonElement element, string name)
    {
        var values = new List<string>();
        if (TryGet(element, name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }
            }
        }

        return values;
    }

    private static string GetString(JsonElement element, params string[] names) =>
        GetStringOrNull(element, names) ?? string.Empty;

    private static string? GetStringOrNull(JsonElement element, params string[] names)
    {
        if (TryGet(element, out var value, names) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static bool GetBool(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool TryGet(JsonElement element, string name, out JsonElement value) =>
        TryGet(element, out value, name);

    /// <summary>
    /// Finds the first of <paramref name="names"/> present on the object, case-insensitively.
    /// <paramref name="names"/> is a priority order, so the loops run names-outer, properties-inner —
    /// properties-outer made the winner an accident of the gateway's serialization order.
    /// </summary>
    private static bool TryGet(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }
}
