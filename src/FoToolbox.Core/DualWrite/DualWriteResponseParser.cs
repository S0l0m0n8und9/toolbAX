using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Parses Dual-write Management gateway JSON responses into strongly-typed models.
/// Field names are reverse-engineered from <c>DWLibary</c>, so reads are deliberately
/// tolerant: property lookups are case-insensitive, arrays may be bare or wrapped in a
/// <c>value</c> envelope, and missing fields degrade to empty rather than throwing.
/// Where a lookup lists several names, the list is a <em>priority order</em> — see
/// <see cref="TryGetProperty"/>.
/// </summary>
public static class DualWriteResponseParser
{
    public static DualWriteEnvironment ParseEnvironment(string json, string identifier)
    {
        using var doc = ParseGatewayJson(json);
        var element = FirstItemOrSelf(doc.RootElement);
        var cid = GetString(element, "cid", "connectionId", "id");
        var cname = GetString(element, "cname", "connectionName", "name");
        return new DualWriteEnvironment(cid, cname, identifier);
    }

    public static IReadOnlyList<DualWriteMap> ParseMaps(string json)
    {
        using var doc = ParseGatewayJson(json);
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
        using var doc = ParseGatewayJson(json);
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

    /// <summary>
    /// Parses the answer to a submitted action. The gateway is inconsistent here — the same submit can
    /// come back as <c>{requestId,…}</c>, as a bare quoted id, or as 202 with no body at all (the sibling
    /// <see cref="DualWriteGatewayClient.SwitchActiveTemplateAsync"/> has always tolerated exactly these).
    /// All three mean "submitted": an id-less answer yields an empty <see cref="DualWriteActionResponse.RequestId"/>
    /// (there is simply nothing to poll), never an exception, because throwing here reported a submitted
    /// action as a failure and skipped the refresh that would have shown it running.
    /// </summary>
    public static DualWriteActionResponse ParseActionResponse(string json)
    {
        var trimmed = json?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new DualWriteActionResponse(string.Empty, null);
        }

        using var doc = ParseGatewayJson(trimmed);
        var element = doc.RootElement;

        // A bare id: the whole body is the request id (quoted, or unquoted when it's numeric).
        if (element.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            return new DualWriteActionResponse(ScalarText(element).Trim(), null);
        }

        var requestId = GetString(element, "requestId", "requestID", "id");
        var state = GetStringOrNull(element, "state", "status");
        return new DualWriteActionResponse(requestId, state);
    }

    public static DualWriteRequestStatus ParseStatus(string json)
    {
        using var doc = ParseGatewayJson(json);
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
        IReadOnlySet<string>? actions = null;
        DualWriteTemplate? active = null;
        var templates = new List<DualWriteTemplate>();
        if (TryGetProperty(item, out var detail, "detail") && detail.ValueKind == JsonValueKind.Object)
        {
            projectId = GetString(detail, "pid", "projectId");
            activeTemplateId = GetString(detail, "tid");
            stateCode = GetString(detail, "state", "mapStatus");
            compositeName = GetString(detail, "tName");
            actions = ReadActionCodes(detail);
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

        actions ??= ReadActionCodes(item);

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
            RightEntityName = rightEntityName,
            Actions = actions
        };
    }

    /// <summary>
    /// Reads the gateway's per-map list of lifecycle actions the map's current state accepts
    /// (<c>detail.actions</c>). The values are action codes in the same numbering the <c>Start</c> request
    /// body uses — <see cref="DualWriteActionType"/>: Start=1, Stop=4, Pause=5, Resume=6, InitialSync=8.
    /// The live-captured <c>Entities</c> response pairs <c>"state":"4"</c> (Running) with
    /// <c>"actions":["4","5"]</c>, i.e. Stop + Pause — exactly the transitions a running map can be asked
    /// for next — while its Stopped sibling (<c>"state":"1"</c>) carries no <c>actions</c> key at all.
    /// <para>
    /// So absent is <em>unknown</em>, not "nothing allowed", and returns null: an older gateway that omits
    /// the field must not have every action refused for it. Codes are kept verbatim (an unrecognised one
    /// is retained, not dropped) and an empty array also degrades to null/unknown.
    /// </para>
    /// </summary>
    private static IReadOnlySet<string>? ReadActionCodes(JsonElement element)
    {
        if (!TryGetProperty(element, out var value, "actions", "availableActions") ||
            value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value.EnumerateArray())
        {
            var code = ScalarText(entry).Trim();
            if (code.Length > 0)
            {
                codes.Add(code);
            }
        }

        return codes.Count == 0 ? null : codes;
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

    /// <summary>
    /// Parses a gateway response body, converting the raw <see cref="JsonException"/> a non-JSON 2xx
    /// produces (a proxy/WAF interstitial, an HTML sign-in page) into a message that names the cause.
    /// The unwrapped exception read "'&lt;' is an invalid start of a value. LineNumber: 0 …", which
    /// mentions neither the gateway nor the fact that something answered in place of it.
    /// </summary>
    internal static JsonDocument ParseGatewayJson(string? json)
    {
        try
        {
            return JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(NonJsonMessage(json), ex);
        }
    }

    private static string NonJsonMessage(string? body) =>
        $"The gateway returned a non-JSON response (HTML sign-in or proxy page?) — first line: {FirstLine(body)}";

    /// <summary>The body's first line, trimmed and capped at 120 characters — enough to recognise an HTML
    /// page or a proxy banner without dumping a whole document (which may carry session detail) into a
    /// message.</summary>
    private static string FirstLine(string? body)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "(empty)";
        }

        var breakAt = text.IndexOfAny(new[] { '\r', '\n' });
        if (breakAt >= 0)
        {
            text = text.Substring(0, breakAt).TrimEnd();
        }

        const int max = 120;
        return text.Length <= max ? text : text.Substring(0, max) + "…";
    }

    private static string ScalarText(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };

    private static string GetString(JsonElement element, params string[] names) =>
        GetStringOrNull(element, names) ?? string.Empty;

    private static string? GetStringOrNull(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        // Present but not a scalar (an object/array/null) reads as absent, so callers expecting a nullable
        // field — state, message — see null rather than an empty string that looks like a real value.
        return value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? ScalarText(value)
            : null;
    }

    /// <summary>
    /// Finds the first of <paramref name="names"/> present on the object, case-insensitively.
    /// <paramref name="names"/> is a <em>priority order</em>: the loops run names-outer, properties-inner,
    /// so the caller's first choice wins regardless of where the gateway happened to serialize it. Running
    /// them the other way round made priority an accident of document order — <c>{"id":…,"cid":…}</c>
    /// returned the environment id as the connection id, which, being non-empty, sailed past the empty-cid
    /// guard and made every subsequent map query come back empty.
    /// </summary>
    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
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
