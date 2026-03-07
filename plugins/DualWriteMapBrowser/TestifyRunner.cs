using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DualWriteMapBrowserPlugin;

public static class TestifyRunner
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildEnumMembersByTypeLookup(IReadOnlyDictionary<string, ODataEnumType> enumLookup)
    {
        return enumLookup
            .Values
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.First().Members, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryBuildPayload(
        ODataEntity entity,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>> enumMembersByType,
        bool enforceMandatory,
        out string json,
        out IReadOnlyList<string> issues)
    {
        var fields = values.Select(v => new ODataFieldValue(v.Key, Include: true, v.Value)).ToList();
        var result = ODataPayloadBuilder.BuildPayloadJson(entity, fields, enumMembersByType, enforceMandatory: enforceMandatory);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Json))
        {
            json = string.Empty;
            issues = result.Issues;
            return false;
        }

        json = result.Json;
        issues = Array.Empty<string>();
        return true;
    }

    public static bool TryBuildEntityInstanceUrl(
        string collectionUrl,
        ODataEntity entity,
        IReadOnlyDictionary<string, string> values,
        out string instanceUrl,
        out string error)
    {
        instanceUrl = string.Empty;
        error = string.Empty;

        var keys = entity.Properties.Where(p => p.IsKey).ToList();
        if (keys.Count == 0)
        {
            error = $"Entity '{entity.Name}' does not expose key metadata.";
            return false;
        }

        var parts = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key.Name, out var keyValue) || string.IsNullOrWhiteSpace(keyValue))
            {
                error = $"Missing key value '{key.Name}' for PATCH URL.";
                return false;
            }

            var literal = BuildODataLiteral(key.Type, keyValue);
            parts.Add($"{key.Name}={literal}");
        }

        var baseUrl = collectionUrl.TrimEnd('/');
        instanceUrl = $"{baseUrl}({string.Join(",", parts)})?cross-company=true";
        return true;
    }

    private static string BuildODataLiteral(string type, string value)
    {
        return type switch
        {
            "Edm.Boolean" => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            "Edm.Int16" or "Edm.Int32" or "Edm.Int64" or "Edm.Decimal" or "Edm.Double" or "Edm.Single" => value,
            "Edm.Guid" => Guid.TryParse(value, out var parsed)
                ? parsed.ToString("D", CultureInfo.InvariantCulture)
                : $"'{EscapeString(value)}'",
            _ => $"'{EscapeString(value)}'"
        };
    }

    private static string EscapeString(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}