using System;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Encodes how dual-write "debug mode" is toggled: per Microsoft's dual-write troubleshooting guidance,
/// it is the <c>IsDebugMode</c> (NoYes) flag on the finance-and-operations <c>DualWriteProjectConfiguration</c>
/// data entity, set over OData. When on, failures are captured to the <c>DualWriteErrorLog</c> table.
///
/// This type is pure (no I/O) so it's fully unit-testable; the view model resolves the entity's OData set
/// from live $metadata and performs the GET/PATCH. The exact OData set name and the field that correlates
/// a map to its project-config row are environment-specific and confirmed at runtime, not hard-coded here.
/// </summary>
public static class DualWriteDebugMode
{
    /// <summary>The finance-and-operations data entity that carries the <c>IsDebugMode</c> flag.</summary>
    public const string EntityLogicalName = "DualWriteProjectConfiguration";

    /// <summary>The NoYes flag field on that entity.</summary>
    public const string DebugField = "IsDebugMode";

    /// <summary>NoYes value for "on".</summary>
    public const string On = "Yes";

    /// <summary>NoYes value for "off".</summary>
    public const string Off = "No";

    /// <summary>The minimal PATCH body that flips debug mode on or off.</summary>
    public static string BuildPatchBody(bool enabled)
        => $"{{\"{DebugField}\":\"{(enabled ? On : Off)}\"}}";

    /// <summary>
    /// Interprets an <c>IsDebugMode</c> value (NoYes "Yes"/"No", a bool, or 1/0) as on/off. Returns null
    /// when the value is absent or unrecognised, so callers can surface "Unknown" rather than guessing.
    /// </summary>
    public static bool? InterpretState(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return rawValue.Trim().ToLowerInvariant() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => null,
        };
    }

    /// <summary>The first config record found for a project: its OData id (for PATCH) and current state.</summary>
    public sealed record DebugRecord(string ODataId, bool? IsDebugMode);

    /// <summary>
    /// Reads the first record out of an OData GET response (a <c>value</c> array, or a single entity),
    /// returning its <c>@odata.id</c> and <c>IsDebugMode</c> state. Null when the response has no record
    /// or no usable id. Never throws on malformed JSON.
    /// </summary>
    public static DebugRecord? ReadFirstRecord(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var record = root;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.Array)
            {
                if (value.GetArrayLength() == 0)
                {
                    return null;
                }

                record = value[0];
            }

            if (record.ValueKind != JsonValueKind.Object
                || !record.TryGetProperty("@odata.id", out var idEl)
                || idEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var odataId = idEl.GetString();
            if (string.IsNullOrWhiteSpace(odataId))
            {
                return null;
            }

            bool? state = null;
            if (record.TryGetProperty(DebugField, out var debugEl))
            {
                state = debugEl.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => InterpretState(debugEl.GetString()),
                    JsonValueKind.Number => debugEl.TryGetInt32(out var n) ? n != 0 : null,
                    _ => null,
                };
            }

            return new DebugRecord(odataId, state);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
