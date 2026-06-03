using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Parses Dual-write Management gateway JSON responses into strongly-typed models.
/// Field names are reverse-engineered from <c>DWLibary</c>, so reads are deliberately
/// tolerant: property lookups are case-insensitive, arrays may be bare or wrapped in a
/// <c>value</c> envelope, and missing fields degrade to empty rather than throwing.
/// </summary>
public static class DualWriteResponseParser
{
    public static DualWriteEnvironment ParseEnvironment(string json, string identifier)
    {
        using var doc = JsonDocument.Parse(json);
        var element = FirstItemOrSelf(doc.RootElement);
        var cid = GetString(element, "cid", "connectionId", "id");
        var cname = GetString(element, "cname", "connectionName", "name");
        return new DualWriteEnvironment(cid, cname, identifier);
    }

    public static IReadOnlyList<DualWriteMap> ParseMaps(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var array = AsArray(doc.RootElement);
        var maps = new List<DualWriteMap>();
        foreach (var item in array)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            maps.Add(ParseMap(item));
        }

        return maps;
    }

    public static IReadOnlyList<DualWriteFieldMapping> ParseFieldMappings(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var array = AsArray(doc.RootElement);
        var mappings = new List<DualWriteFieldMapping>();
        foreach (var item in array)
        {
            var name = item.ValueKind == JsonValueKind.Object
                ? GetString(item, "name", "fieldMappingName")
                : item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                mappings.Add(new DualWriteFieldMapping(name));
            }
        }

        return mappings;
    }

    public static DualWriteActionResponse ParseActionResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement;
        var requestId = GetString(element, "requestId", "requestID", "id");
        var state = GetStringOrNull(element, "state", "status");
        return new DualWriteActionResponse(requestId, state);
    }

    public static DualWriteRequestStatus ParseStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement;
        var requestId = GetString(element, "requestId", "requestID", "id");
        var state = GetString(element, "state", "status");
        var message = GetStringOrNull(element, "message", "error", "errorMessage", "details");
        var (isTerminal, isSuccess) = DualWriteStatusInterpreter.Classify(state);
        return new DualWriteRequestStatus(requestId, state, isTerminal, isSuccess, message);
    }

    private static DualWriteMap ParseMap(JsonElement item)
    {
        // The gateway "Entities" item (DWLibary DWMap) nests the map under leftEntity/rightEntity
        // and a "detail" block; the older flat shape (top-level name/state/template) is kept only
        // as a tolerant fallback. leftEntity is the F&O entity; rightEntity is the CE table.
        var leftEntityName = string.Empty;
        var leftEntityDisplay = string.Empty;
        if (TryGetProperty(item, out var leftEntity, "leftEntity") && leftEntity.ValueKind == JsonValueKind.Object)
        {
            leftEntityName = GetString(leftEntity, "name");
            leftEntityDisplay = GetString(leftEntity, "displayName", "displayname");
        }

        var rightEntityName = string.Empty;
        if (TryGetProperty(item, out var rightEntity, "rightEntity") && rightEntity.ValueKind == JsonValueKind.Object)
        {
            rightEntityName = GetString(rightEntity, "name");
        }

        var projectId = string.Empty;
        var activeTemplateId = string.Empty;
        var stateCode = string.Empty;
        var compositeName = string.Empty;
        DualWriteTemplate? active = null;
        var templates = new List<DualWriteTemplate>();
        if (TryGetProperty(item, out var detail, "detail") && detail.ValueKind == JsonValueKind.Object)
        {
            projectId = GetString(detail, "pid", "projectId");
            activeTemplateId = GetString(detail, "tid");
            stateCode = GetString(detail, "state", "mapStatus");
            compositeName = GetString(detail, "tName");
            if (TryGetProperty(detail, out var templateEl, "template") && templateEl.ValueKind == JsonValueKind.Object)
            {
                active = ParseTemplate(templateEl);
            }

            if (TryGetProperty(detail, out var templatesEl, "templates") && templatesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in templatesEl.EnumerateArray())
                {
                    if (t.ValueKind == JsonValueKind.Object)
                    {
                        templates.Add(ParseTemplate(t));
                    }
                }
            }
        }

        // Tolerant fallbacks to the older assumed flat shape.
        if (string.IsNullOrWhiteSpace(projectId))
        {
            projectId = GetString(item, "pid", "projectId");
        }

        if (active is null && TryGetProperty(item, out var flatTemplate, "template") && flatTemplate.ValueKind == JsonValueKind.Object)
        {
            active = ParseTemplate(flatTemplate);
        }

        // Map identity (Name): the gateway's composite "tName" (e.g. "accounts - Customers V3") is
        // unique per left+right pair — unlike the F&O entity name alone, which repeats across CE
        // targets — so it's the stable key for compare/export. DisplayName shows the friendlier F&O
        // (left) entity name in the grid; the CE Entity column disambiguates same-named rows.
        var name = FirstNonEmpty(compositeName, leftEntityName, GetString(item, "name"));
        var displayName = FirstNonEmpty(leftEntityDisplay, leftEntityName, compositeName, GetString(item, "displayName", "displayname"), name);

        // Id must be the active template id so lifecycle actions (Start/Stop/...) send a valid tid.
        var id = FirstNonEmpty(active?.Id, activeTemplateId, GetString(item, "id", "templateId", "msdyn_dualwriteentitymapid"));

        var state = DescribeMapState(FirstNonEmpty(stateCode, GetString(item, "state", "status", "executionState")));

        return new DualWriteMap(id, name, displayName, projectId, state, active, templates)
        {
            RightEntityName = rightEntityName
        };
    }

    private static DualWriteTemplate ParseTemplate(JsonElement element)
    {
        var id = GetString(element, "id", "templateId");
        var version = FormatVersion(element);
        var author = GetString(element, "author", "createdBy");
        return new DualWriteTemplate(id, version, author);
    }

    /// <summary>
    /// Formats a template version. The gateway returns a structured object
    /// (<c>{major,minor,build,revision}</c>, DWLibary <c>DWMapVersion</c>); a plain string or
    /// number is also accepted for tolerance.
    /// </summary>
    private static string FormatVersion(JsonElement template)
    {
        if (!TryGetProperty(template, out var version, "version"))
        {
            return string.Empty;
        }

        switch (version.ValueKind)
        {
            case JsonValueKind.Object:
                static int Part(JsonElement obj, string name) =>
                    obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)
                        ? i
                        : 0;
                var major = Part(version, "major");
                var minor = Part(version, "minor");
                var build = Part(version, "build");
                var revision = Part(version, "revision");
                // An empty ({}) or all-zero version object is "no version" — render blank rather than
                // a misleading "0.0.0.0", matching the blank an absent version property already gives.
                return major == 0 && minor == 0 && build == 0 && revision == 0
                    ? string.Empty
                    : $"{major}.{minor}.{build}.{revision}";
            case JsonValueKind.String:
                return version.GetString() ?? string.Empty;
            case JsonValueKind.Number:
                return version.GetRawText();
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Maps a numeric <c>MapStatus</c> code (DWLibary <c>DWEnums.MapStatus</c>) to a friendly
    /// name. Already-friendly strings pass through; unknown codes are returned verbatim rather
    /// than blanked, so nothing is silently dropped.
    /// </summary>
    private static string DescribeMapState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return string.Empty;
        }

        return state.Trim() switch
        {
            "0" => "None",
            "1" => "Stopped",
            "2" => "Initial sync",
            "3" => "Catch-up",
            "4" => "Running",
            "5" => "Paused",
            "6" => "Not running",
            var other => other
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static JsonElement FirstItemOrSelf(JsonElement root)
    {
        var array = AsArrayOrNull(root);
        if (array is not null)
        {
            foreach (var item in array.Value.EnumerateArray())
            {
                return item;
            }
        }

        return root;
    }

    private static IEnumerable<JsonElement> AsArray(JsonElement root)
    {
        var array = AsArrayOrNull(root);
        return array is null ? Array.Empty<JsonElement>() : array.Value.EnumerateArray();
    }

    private static JsonElement? AsArrayOrNull(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            TryGetProperty(root, out var value, "value", "entities", "items") &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        return null;
    }

    private static string GetString(JsonElement element, params string[] names) =>
        GetStringOrNull(element, names) ?? string.Empty;

    private static string? GetStringOrNull(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var name in names)
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
