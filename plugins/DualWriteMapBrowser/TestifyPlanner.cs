using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DualWriteMapBrowserPlugin;

public static class TestifyPlanner
{
    public sealed record LookupValidationIssue(string FieldLabel, string RelatedTable, string ProvidedValue);

    public static Dictionary<string, string> ExtractMapPropertyCandidates(string? mappingRaw, string? propertiesRaw)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // mapping JSON contains transform rules (legs/valueMaps). Treat it conservatively.
        AddJsonValues(mappingRaw, values, skipMappingLegs: true);
        AddJsonValues(propertiesRaw, values, skipMappingLegs: false);
        return values;
    }

    public static Dictionary<string, string> NormalizeMapProperties(
        IReadOnlyDictionary<string, string> rawValues,
        IReadOnlyList<ODataProperty> foProperties,
        out List<string> warnings)
    {
        warnings = new List<string>();
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var foLookup = foProperties
            .GroupBy(p => NormalizeKey(p.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in rawValues)
        {
            var normalizedKey = NormalizeKey(pair.Key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                continue;
            }

            if (!foLookup.TryGetValue(normalizedKey, out var actualName))
            {
                warnings.Add($"Ignored map property '{pair.Key}' because no FO metadata field matched.");
                continue;
            }

            normalized[actualName] = pair.Value;
        }

        return normalized;
    }

    public static Dictionary<string, string> ExtractEqualityConstraints(string? foFilter)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(foFilter))
        {
            return values;
        }

        foreach (Match match in Regex.Matches(
                     foFilter,
                     @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+eq\s+(?<enumType>[A-Za-z_][A-Za-z0-9_.]*)'(?<enumValue>(?:[^']|'')*)'",
                     RegexOptions.IgnoreCase))
        {
            var field = match.Groups["field"].Value;
            var value = match.Groups["enumValue"].Value.Replace("''", "'", StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(field))
            {
                values[field] = value;
            }
        }

        foreach (Match match in Regex.Matches(
                     foFilter,
                     @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+eq\s+'(?<value>(?:[^']|'')*)'",
                     RegexOptions.IgnoreCase))
        {
            var field = match.Groups["field"].Value;
            var value = match.Groups["value"].Value.Replace("''", "'", StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(field))
            {
                values[field] = value;
            }
        }

        foreach (Match match in Regex.Matches(
                     foFilter,
                     @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+eq\s+(?<value>true|false|-?\d+(?:\.\d+)?)\b",
                     RegexOptions.IgnoreCase))
        {
            var field = match.Groups["field"].Value;
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(field) && !values.ContainsKey(field))
            {
                values[field] = value;
            }
        }

        return values;
    }

    public static IReadOnlyList<TestifyPatchStep> BuildMinimalPatchSteps(
        IReadOnlyDictionary<string, IReadOnlyList<string>> enumMembersByField,
        IReadOnlyDictionary<string, string>? fixedValues = null)
    {
        var normalizedMembers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in enumMembersByField)
        {
            if (pair.Value.Count == 0)
            {
                continue;
            }

            if (fixedValues is not null && fixedValues.TryGetValue(pair.Key, out var fixedValue) && !string.IsNullOrWhiteSpace(fixedValue))
            {
                normalizedMembers[pair.Key] = new[] { fixedValue };
            }
            else
            {
                normalizedMembers[pair.Key] = pair.Value;
            }
        }

        if (normalizedMembers.Count == 0)
        {
            return Array.Empty<TestifyPatchStep>();
        }

        var maxCardinality = normalizedMembers.Max(p => p.Value.Count);
        if (maxCardinality <= 1)
        {
            return Array.Empty<TestifyPatchStep>();
        }

        var steps = new List<TestifyPatchStep>(maxCardinality - 1);
        for (var step = 1; step < maxCardinality; step++)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in normalizedMembers)
            {
                var memberIndex = Math.Min(step, pair.Value.Count - 1);
                values[pair.Key] = pair.Value[memberIndex];
            }

            steps.Add(new TestifyPatchStep(step, values));
        }

        return steps;
    }

    public static IReadOnlyList<string> ValidateFixedEnumCoverage(
        IReadOnlyDictionary<string, IReadOnlyList<string>> enumMembersByField,
        IReadOnlyDictionary<string, string> fixedValues)
    {
        var issues = new List<string>();
        foreach (var pair in fixedValues)
        {
            if (!enumMembersByField.TryGetValue(pair.Key, out var members) || members.Count == 0)
            {
                continue;
            }

            if (!members.Contains(pair.Value, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add($"Filter fixed enum value '{pair.Value}' is not valid for field '{pair.Key}'.");
                continue;
            }

            if (members.Count > 1)
            {
                issues.Add($"Filter pins enum field '{pair.Key}' to '{pair.Value}', preventing full enum coverage.");
            }
        }

        return issues;
    }

    public static string TrimToMaxLength(ODataProperty property, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!int.TryParse(property.MaxLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLen) || maxLen <= 0)
        {
            return value;
        }

        return value.Length <= maxLen ? value : value[..maxLen];
    }

    public static string? GenerateDefaultValue(
        ODataProperty property,
        string runToken,
        IReadOnlyDictionary<string, IReadOnlyList<string>> enumMembersByType,
        string? defaultCompany)
    {
        if (string.Equals(property.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase))
        {
            // Never synthesize fake company values. If no default company is configured,
            // caller should block and ask for a valid company source.
            return string.IsNullOrWhiteSpace(defaultCompany) ? null : defaultCompany;
        }

        if (!string.IsNullOrWhiteSpace(property.Type) && !property.Type.StartsWith("Edm.", StringComparison.OrdinalIgnoreCase))
        {
            if (enumMembersByType.TryGetValue(property.Type, out var members) && members.Count > 0)
            {
                return members[0];
            }
        }

        return property.Type switch
        {
            "Edm.Boolean" => "true",
            "Edm.Int16" or "Edm.Int32" or "Edm.Int64" => "1",
            "Edm.Decimal" or "Edm.Double" or "Edm.Single" => "1",
            "Edm.Date" => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "Edm.DateTimeOffset" => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            "Edm.Guid" => Guid.NewGuid().ToString(),
            _ => $"{runToken}_{property.Name}"
        };
    }

    public static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    public static IReadOnlyList<string> ExtractMandatoryFieldLabels(string errorBody)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return Array.Empty<string>();
        }

        foreach (Match match in Regex.Matches(
                     errorBody,
                     @"Mandatory field '(?<label>[^']+)' not set",
                     RegexOptions.IgnoreCase))
        {
            var label = match.Groups["label"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(label))
            {
                labels.Add(label);
            }
        }

        return labels.ToList();
    }

    public static IReadOnlyList<LookupValidationIssue> ExtractLookupValidationIssues(string errorBody)
    {
        var issues = new List<LookupValidationIssue>();
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return issues;
        }

        foreach (Match match in Regex.Matches(
                     errorBody,
                     @"The value '(?<value>[^']*)' in field '(?<field>[^']+)' is not found in the related table '(?<table>[^']+)'",
                     RegexOptions.IgnoreCase))
        {
            var field = match.Groups["field"].Value.Trim();
            var table = match.Groups["table"].Value.Trim();
            var value = match.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(table))
            {
                continue;
            }

            issues.Add(new LookupValidationIssue(field, table, value));
        }

        return issues;
    }

    public static string? ResolveFieldByLabel(
        string label,
        IReadOnlyList<ODataProperty> properties,
        IReadOnlyDictionary<string, string> currentValues)
    {
        if (string.IsNullOrWhiteSpace(label) || properties.Count == 0)
        {
            return null;
        }

        var normalizedLabel = NormalizeKey(label);
        if (string.IsNullOrWhiteSpace(normalizedLabel))
        {
            return null;
        }

        var direct = properties.FirstOrDefault(p =>
            !p.IsKey &&
            string.Equals(NormalizeKey(p.Name), normalizedLabel, StringComparison.OrdinalIgnoreCase));
        if (direct is not null && IsMissing(currentValues, direct.Name))
        {
            return direct.Name;
        }

        var labelTokens = Tokenize(label).ToList();
        if (labelTokens.Count == 0)
        {
            return null;
        }

        var ranked = properties
            .Where(p => !p.IsKey && string.Equals(p.Type, "Edm.String", StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var propTokens = Tokenize(p.Name).ToList();
                var overlap = propTokens.Intersect(labelTokens, StringComparer.OrdinalIgnoreCase).Count();
                var score = overlap * 20 - Math.Abs(propTokens.Count - labelTokens.Count) * 3;
                if (p.Name.Contains("Name", StringComparison.OrdinalIgnoreCase) &&
                    label.Contains("name", StringComparison.OrdinalIgnoreCase))
                {
                    score += 5;
                }

                if (!IsMissing(currentValues, p.Name))
                {
                    score -= 100;
                }

                return (Property: p.Name, Score: score);
            })
            .OrderByDescending(r => r.Score)
            .ToList();

        if (ranked.Count == 0 || ranked[0].Score <= 0)
        {
            return null;
        }

        return ranked[0].Property;
    }

    private static void AddJsonValues(string? rawJson, Dictionary<string, string> values, bool skipMappingLegs)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        var trimmed = rawJson.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            AppendJsonPrimitiveValues(document.RootElement, values, path: "$", skipMappingLegs: skipMappingLegs);
        }
        catch (JsonException)
        {
            // Ignore malformed JSON here; caller records failures elsewhere.
        }
    }

    private static void AppendJsonPrimitiveValues(
        JsonElement element,
        Dictionary<string, string> values,
        string path,
        bool skipMappingLegs)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var isTopLevelLegs = skipMappingLegs &&
                                     string.Equals(path, "$", StringComparison.Ordinal) &&
                                     string.Equals(property.Name, "legs", StringComparison.OrdinalIgnoreCase);
                var isTransformTree = skipMappingLegs &&
                                      (string.Equals(property.Name, "valueTransforms", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(property.Name, "valueMap", StringComparison.OrdinalIgnoreCase));
                if (isTopLevelLegs || isTransformTree)
                {
                    continue;
                }

                if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();
                }

                var childPath = path == "$" ? $"$.{property.Name}" : $"{path}.{property.Name}";
                AppendJsonPrimitiveValues(property.Value, values, childPath, skipMappingLegs);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                AppendJsonPrimitiveValues(item, values, $"{path}[{index}]", skipMappingLegs);
                index++;
            }
        }
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var withBoundaries = Regex.Replace(value, @"([a-z])([A-Z])", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"([A-Za-z])(\d)", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"(\d)([A-Za-z])", "$1 $2");

        foreach (Match match in Regex.Matches(withBoundaries, @"[A-Za-z0-9]+"))
        {
            var token = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                yield return token.ToLowerInvariant();
            }
        }
    }

    private static bool IsMissing(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var existing))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(existing);
    }
}
