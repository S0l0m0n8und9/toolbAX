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
        var id = GetString(item, "id", "templateId", "msdyn_dualwriteentitymapid");
        var name = GetString(item, "name");
        var displayName = GetString(item, "displayName", "displayname");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = name;
        }

        var state = GetString(item, "state", "status", "executionState");

        var projectId = string.Empty;
        var templates = new List<DualWriteTemplate>();
        if (TryGetProperty(item, out var detail, "detail") && detail.ValueKind == JsonValueKind.Object)
        {
            projectId = GetString(detail, "pid", "projectId");
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

        DualWriteTemplate? active = null;
        if (TryGetProperty(item, out var templateEl, "template") && templateEl.ValueKind == JsonValueKind.Object)
        {
            active = ParseTemplate(templateEl);
        }

        return new DualWriteMap(id, name, displayName, projectId, state, active, templates);
    }

    private static DualWriteTemplate ParseTemplate(JsonElement element)
    {
        var id = GetString(element, "id", "templateId");
        var version = GetString(element, "version");
        var author = GetString(element, "author", "createdBy");
        return new DualWriteTemplate(id, version, author);
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
