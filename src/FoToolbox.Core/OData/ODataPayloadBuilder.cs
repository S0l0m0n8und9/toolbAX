using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FoToolbox.Core.OData;

public sealed record ODataFieldValue(string Name, bool Include, string? Value);

public sealed record ODataPayloadBuildResult(bool Ok, string? Json, IReadOnlyList<string> Issues);

public static class ODataPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static ODataPayloadBuildResult BuildPayloadJson(
        ODataEntity entity,
        IEnumerable<ODataFieldValue> fieldValues,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? enumMembersByType = null,
        IReadOnlyDictionary<string, string>? defaultValues = null,
        bool enforceMandatory = true)
    {
        if (entity is null) throw new ArgumentNullException(nameof(entity));
        if (fieldValues is null) throw new ArgumentNullException(nameof(fieldValues));

        var issues = new List<string>();
        var map = fieldValues
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var known = new HashSet<string>(entity.Properties.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var extra in map.Keys.Where(k => !known.Contains(k)))
        {
            if (map[extra].Include)
            {
                issues.Add($"Unknown field '{extra}' for entity {entity.Name}.");
            }
        }

        var obj = new JsonObject();

        foreach (var prop in entity.Properties)
        {
            var inputExists = map.TryGetValue(prop.Name, out var input);
            var include = inputExists ? input!.Include : prop.Mandatory;

            if (!include) continue;

            var raw = inputExists ? input!.Value : null;
            if (string.IsNullOrWhiteSpace(raw) && defaultValues is not null && defaultValues.TryGetValue(prop.Name, out var def))
            {
                raw = def;
            }

            var trimmed = raw?.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (enforceMandatory && prop.Mandatory)
                {
                    issues.Add($"Field '{prop.Name}' is mandatory and must have a value.");
                }
                continue; // optional blank omitted
            }

            if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
            {
                if (!prop.Nullable)
                {
                    issues.Add($"Field '{prop.Name}' is not nullable.");
                    continue;
                }

                obj[prop.Name] = null;
                continue;
            }

            if (enumMembersByType is not null && enumMembersByType.TryGetValue(prop.Type, out var enumMembers))
            {
                if (!enumMembers.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add($"Field '{prop.Name}' value '{trimmed}' is not a valid enum member for {prop.Type}.");
                    continue;
                }
                obj[prop.Name] = trimmed;
                continue;
            }

            if (!TryParseJsonValue(prop.Type, trimmed, out var node, out var issue))
            {
                issues.Add($"Field '{prop.Name}': {issue}");
                continue;
            }

            obj[prop.Name] = node;
        }

        if (issues.Count > 0)
        {
            return new ODataPayloadBuildResult(false, null, issues);
        }

        return new ODataPayloadBuildResult(true, obj.ToJsonString(JsonOptions), Array.Empty<string>());
    }

    private static bool TryParseJsonValue(string odataType, string value, out JsonNode? node, out string issue)
    {
        node = null;
        issue = "Invalid value.";

        var t = (odataType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t))
        {
            node = value;
            issue = string.Empty;
            return true;
        }

        // Primitive Edm types we know how to validate/convert.
        switch (t)
        {
            case "Edm.Boolean":
                if (TryParseBool(value, out var b))
                {
                    node = b;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a boolean (true/false).";
                return false;

            case "Edm.Int16":
            case "Edm.Int32":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    node = i;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected an integer.";
                return false;

            case "Edm.Int64":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    node = l;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a 64-bit integer.";
                return false;

            case "Edm.Decimal":
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                {
                    node = d;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a decimal number.";
                return false;

            case "Edm.Double":
                if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dbl))
                {
                    node = dbl;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a double.";
                return false;

            case "Edm.Single":
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var f))
                {
                    node = f;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a float.";
                return false;

            case "Edm.Guid":
                if (Guid.TryParse(value, out var g))
                {
                    node = g.ToString();
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a GUID.";
                return false;

            case "Edm.Date":
                if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    node = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a date (yyyy-MM-dd).";
                return false;

            case "Edm.DateTimeOffset":
                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                {
                    node = dto.ToString("O", CultureInfo.InvariantCulture);
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a DateTimeOffset (ISO 8601).";
                return false;

            default:
                node = value;
                issue = string.Empty;
                return true;
        }
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (bool.TryParse(value, out result)) return true;
        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)) { result = false; return true; }
        return false;
    }
}
