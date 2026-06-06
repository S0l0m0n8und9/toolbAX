using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Reshapes a Dataverse <c>msdyn_dualwriteentitymaps</c> Web API response into <see cref="DwMapRecord"/>s
/// for the Dual-Write Map Browser. Faithfully ports the WPF plugin's record/mapping parsing (FormattedValue
/// option-set fallbacks; nested <c>msdyn_mapping</c> legs/fields/value-transforms; flattened
/// <c>msdyn_properties</c>). Pure, side-effect-free — no UI, no HTTP — so it is unit-testable on any OS.
/// </summary>
public static class DualWriteMapParser
{
    /// <summary>Columns selected from <c>msdyn_dualwriteentitymap</c> (matches the WPF Map Browser).</summary>
    public static readonly string SelectColumns = string.Join(",",
        "msdyn_dualwriteentitymapid",
        "solutionid",
        "msdyn_name",
        "msdyn_displayname",
        "msdyn_mapping",
        "msdyn_properties",
        "msdyn_version",
        "createdon",
        "modifiedon",
        "statecode",
        "statuscode",
        "ownerid");

    /// <summary>The relative Web API path for the first page of dual-write maps (newest first).</summary>
    public static string MapsPath() =>
        $"msdyn_dualwriteentitymaps?$select={Uri.EscapeDataString(SelectColumns)}&$orderby=modifiedon%20desc";

    /// <summary>Component type for a dual-write entity map in <c>solutioncomponents</c>.</summary>
    public const int DualWriteMapComponentType = 500;

    private static readonly string SolutionSelectColumns = string.Join(",",
        "solutionid", "uniquename", "friendlyname", "version", "_publisherid_value");

    /// <summary>The relative Web API path for the first page of solutions (with publisher expanded).</summary>
    public static string SolutionsPath() =>
        $"solutions?$select={Uri.EscapeDataString(SolutionSelectColumns)}" +
        "&$expand=publisherid($select=uniquename,friendlyname)&$orderby=uniquename%20asc";

    /// <summary>The relative Web API path for a solution's dual-write-map components (object ids).</summary>
    public static string SolutionComponentsPath(string solutionUniqueName)
    {
        var escaped = (solutionUniqueName ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
        var filter = $"(componenttype eq {DualWriteMapComponentType}) and (solutionid/uniquename eq '{escaped}')";
        return $"solutioncomponents?$select=objectid&$filter={Uri.EscapeDataString(filter)}";
    }

    /// <summary>Parses one page of solutions (tolerates null/blank/malformed input).</summary>
    public static DwSolutionPage ParseSolutionPage(string? json)
    {
        if (!TryGetValueArray(json, out var valueArray, out var root))
        {
            return new DwSolutionPage(Array.Empty<DwSolution>(), null);
        }

        var solutions = new List<DwSolution>();
        foreach (var item in valueArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                solutions.Add(ParseSolution(item));
            }
        }

        return new DwSolutionPage(solutions, GetValueAsString(root, "@odata.nextLink"));
    }

    /// <summary>Parses one page of solution-component object ids (tolerates null/blank/malformed input).</summary>
    public static DwComponentIdPage ParseComponentIdPage(string? json)
    {
        if (!TryGetValueArray(json, out var valueArray, out var root))
        {
            return new DwComponentIdPage(Array.Empty<Guid>(), null);
        }

        var ids = new List<Guid>();
        foreach (var item in valueArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && Guid.TryParse(GetValueAsString(item, "objectid"), out var id))
            {
                ids.Add(id);
            }
        }

        return new DwComponentIdPage(ids, GetValueAsString(root, "@odata.nextLink"));
    }

    private static DwSolution ParseSolution(JsonElement item)
    {
        var publisherUnique = string.Empty;
        var publisherDisplay = GetValueAsString(item, "_publisherid_value@OData.Community.Display.V1.FormattedValue")
            ?? string.Empty;
        if (item.TryGetProperty("publisherid", out var publisher) && publisher.ValueKind == JsonValueKind.Object)
        {
            publisherUnique = GetValueAsString(publisher, "uniquename") ?? string.Empty;
            var friendly = GetValueAsString(publisher, "friendlyname") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(friendly))
            {
                publisherDisplay = friendly;
            }
        }

        if (string.IsNullOrWhiteSpace(publisherUnique))
        {
            publisherUnique = GetValueAsString(item, "_publisherid_value") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(publisherDisplay))
        {
            publisherDisplay = string.IsNullOrWhiteSpace(publisherUnique) ? "(Unknown Publisher)" : publisherUnique;
        }

        return new DwSolution(
            Id: GetValueAsString(item, "solutionid") ?? string.Empty,
            UniqueName: GetValueAsString(item, "uniquename") ?? string.Empty,
            FriendlyName: GetValueAsString(item, "friendlyname") ?? string.Empty,
            Version: GetValueAsString(item, "version") ?? string.Empty,
            PublisherUniqueName: publisherUnique,
            PublisherDisplayName: publisherDisplay);
    }

    private static bool TryGetValueArray(string? json, out JsonElement valueArray, out JsonElement root)
    {
        valueArray = default;
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("value", out valueArray)
            && valueArray.ValueKind == JsonValueKind.Array;
    }

    /// <summary>
    /// Parses one Web API response page. Tolerates null/blank/malformed input (returns an empty page)
    /// so a transient bad response never throws into the UI.
    /// </summary>
    public static DwMapPage ParsePage(string? json)
    {
        if (!TryGetValueArray(json, out var valueArray, out var root))
        {
            return new DwMapPage(Array.Empty<DwMapRecord>(), null);
        }

        var records = new List<DwMapRecord>();
        foreach (var item in valueArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                records.Add(ParseRecord(item));
            }
        }

        return new DwMapPage(records, GetValueAsString(root, "@odata.nextLink"));
    }

    private static DwMapRecord ParseRecord(JsonElement item)
    {
        var state = GetValueAsString(item, "statecode@OData.Community.Display.V1.FormattedValue")
            ?? GetValueAsString(item, "statecodename")
            ?? GetValueAsString(item, "statecode")
            ?? string.Empty;

        var status = GetValueAsString(item, "statuscode@OData.Community.Display.V1.FormattedValue")
            ?? GetValueAsString(item, "statuscodename")
            ?? GetValueAsString(item, "statuscode")
            ?? string.Empty;

        var owner = GetValueAsString(item, "_ownerid_value@OData.Community.Display.V1.FormattedValue")
            ?? GetValueAsString(item, "owneridname")
            ?? GetValueAsString(item, "_ownerid_value")
            ?? GetValueAsString(item, "ownerid")
            ?? string.Empty;

        var mappingRaw = GetValueAsString(item, "msdyn_mapping");
        var propertiesRaw = GetValueAsString(item, "msdyn_properties");
        var mappingRoot = TryParseJsonElement(mappingRaw);
        var propertiesRoot = TryParseJsonElement(propertiesRaw);

        return new DwMapRecord(
            Id: GetValueAsString(item, "msdyn_dualwriteentitymapid") ?? string.Empty,
            SolutionId: GetValueAsString(item, "solutionid") ?? string.Empty,
            Name: GetValueAsString(item, "msdyn_name") ?? string.Empty,
            DisplayName: GetValueAsString(item, "msdyn_displayname") ?? string.Empty,
            Version: GetValueAsString(item, "msdyn_version") ?? string.Empty,
            State: state,
            Status: status,
            Owner: owner,
            CreatedOn: ParseDate(GetValueAsString(item, "createdon")),
            ModifiedOn: ParseDate(GetValueAsString(item, "modifiedon")),
            SummaryRows: BuildSummaryRows(mappingRoot),
            Legs: BuildLegs(mappingRoot),
            Fields: BuildFields(mappingRoot),
            ValueTransforms: BuildValueTransforms(mappingRoot),
            Properties: BuildProperties(propertiesRoot, propertiesRaw),
            RawMapping: mappingRaw,
            RawProperties: propertiesRaw);
    }

    private static IReadOnlyList<DwMapSummaryRow> BuildSummaryRows(JsonElement? mappingRoot)
    {
        if (mappingRoot is null || mappingRoot.Value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<DwMapSummaryRow>();
        }

        var rows = new List<DwMapSummaryRow>();
        foreach (var property in mappingRoot.Value.EnumerateObject())
        {
            if (property.NameEquals("legs"))
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    rows.Add(new DwMapSummaryRow("legs.count",
                        property.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture)));
                }
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            rows.Add(new DwMapSummaryRow(property.Name, GetPrimitiveValue(property.Value)));
        }

        return rows;
    }

    private static IReadOnlyList<DwMapLeg> BuildLegs(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<DwMapLeg>();
        }

        var rows = new List<DwMapLeg>();
        foreach (var leg in legs.EnumerateArray())
        {
            var fieldCount = 0;
            if (leg.TryGetProperty("fieldMappings", out var fieldMappings) && fieldMappings.ValueKind == JsonValueKind.Array)
            {
                fieldCount = fieldMappings.GetArrayLength();
            }

            var sourceFilter = GetJsonString(leg, "sourceFilter");
            rows.Add(new DwMapLeg(
                LegId: GetJsonString(leg, "id"),
                SourceSchema: GetJsonString(leg, "sourceSchema"),
                SourceSchemaDistinctName: GetJsonString(leg, "sourceSchemaDistinctName"),
                DestinationSchema: GetJsonString(leg, "destinationSchema"),
                SourceEnvironmentType: GetJsonString(leg, "sourceEnvironmentType"),
                DestinationEnvironmentType: GetJsonString(leg, "destinationEnvironmentType"),
                SourceFilter: sourceFilter,
                ReversedSourceFilter: GetJsonString(leg, "reversedSourceFilter"),
                SourceFilterOData: DualWriteFilterConverter.XppToOData(sourceFilter),
                FieldMappings: fieldCount));
        }

        return rows;
    }

    private static IReadOnlyList<DwMapField> BuildFields(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<DwMapField>();
        }

        var rows = new List<DwMapField>();
        foreach (var leg in legs.EnumerateArray())
        {
            var legId = GetJsonString(leg, "id");
            var sourceSchema = GetJsonString(leg, "sourceSchema");
            var destinationSchema = GetJsonString(leg, "destinationSchema");

            if (!leg.TryGetProperty("fieldMappings", out var fieldMappings) || fieldMappings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var mapping in fieldMappings.EnumerateArray())
            {
                var syncDirection = mapping.TryGetProperty("syncDirection", out var dir)
                    ? dir.ToString()
                    : string.Empty;

                var valueTransforms = 0;
                if (mapping.TryGetProperty("valueTransforms", out var transforms) && transforms.ValueKind == JsonValueKind.Array)
                {
                    valueTransforms = transforms.GetArrayLength();
                }

                rows.Add(new DwMapField(
                    LegId: legId,
                    SourceSchema: sourceSchema,
                    DestinationSchema: destinationSchema,
                    SyncDirection: syncDirection,
                    SourceField: GetJsonString(mapping, "sourceField"),
                    DestinationField: GetJsonString(mapping, "destinationField"),
                    DestinationLookupEntity: GetJsonString(mapping, "destinationLookupFieldRelatedEntity"),
                    IsSystemGenerated: GetJsonBool(mapping, "isSystemGenerated"),
                    ValueTransforms: valueTransforms));
            }
        }

        return rows;
    }

    private static IReadOnlyList<DwMapValueTransform> BuildValueTransforms(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<DwMapValueTransform>();
        }

        var rows = new List<DwMapValueTransform>();
        foreach (var leg in legs.EnumerateArray())
        {
            var legId = GetJsonString(leg, "id");
            if (!leg.TryGetProperty("fieldMappings", out var fieldMappings) || fieldMappings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var mapping in fieldMappings.EnumerateArray())
            {
                var sourceField = GetJsonString(mapping, "sourceField");
                var destinationField = GetJsonString(mapping, "destinationField");

                if (!mapping.TryGetProperty("valueTransforms", out var transforms) || transforms.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var transform in transforms.EnumerateArray())
                {
                    var valueMap = string.Empty;
                    if (transform.TryGetProperty("valueMap", out var valueMapElement) &&
                        valueMapElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        valueMap = JsonSerializer.Serialize(valueMapElement);
                    }

                    var hasDefaultValue = transform.TryGetProperty("defaultValue", out var defaultValueElement);
                    var defaultValue = hasDefaultValue ? GetNullablePrimitiveValue(defaultValueElement) : null;

                    rows.Add(new DwMapValueTransform(
                        LegId: legId,
                        SourceField: sourceField,
                        DestinationField: destinationField,
                        TransformType: GetJsonString(transform, "transformType"),
                        DefaultValue: defaultValue,
                        HasDefaultValue: hasDefaultValue,
                        ValueMap: valueMap,
                        CreateValuesOnDestination: GetJsonBool(transform, "createValuesOnDestination")));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<DwMapProperty> BuildProperties(JsonElement? propertiesRoot, string? fallbackRaw)
    {
        if (propertiesRoot is null)
        {
            if (string.IsNullOrWhiteSpace(fallbackRaw))
            {
                return Array.Empty<DwMapProperty>();
            }

            return new[] { new DwMapProperty("$", "String", fallbackRaw) };
        }

        var root = propertiesRoot.Value;
        if (root.ValueKind == JsonValueKind.Object)
        {
            var rows = new List<DwMapProperty>();
            foreach (var property in root.EnumerateObject())
            {
                var value = property.Value;
                rows.Add(new DwMapProperty(
                    Key: property.Name,
                    Type: value.ValueKind.ToString(),
                    Value: value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        ? JsonSerializer.Serialize(value)
                        : GetPrimitiveValue(value)));
            }

            return rows;
        }

        return new[]
        {
            new DwMapProperty("$", root.ValueKind.ToString(),
                root.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? JsonSerializer.Serialize(root)
                    : GetPrimitiveValue(root)),
        };
    }

    private static bool TryGetLegsArray(JsonElement? mappingRoot, out JsonElement legs)
    {
        legs = default;
        if (mappingRoot is null || mappingRoot.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return mappingRoot.Value.TryGetProperty("legs", out legs) && legs.ValueKind == JsonValueKind.Array;
    }

    private static string? GetValueAsString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.ToString(),
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static JsonElement? TryParseJsonElement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetPrimitiveValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => element.ToString(),
    };

    private static string? GetNullablePrimitiveValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => element.ToString(),
    };

    private static string GetJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? GetPrimitiveValue(value) : string.Empty;

    private static bool? GetJsonBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
