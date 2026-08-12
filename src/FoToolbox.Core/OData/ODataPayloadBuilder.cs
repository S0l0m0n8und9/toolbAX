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

    // Every value below is parsed with InvariantCulture, so the hazard isn't culture-specific formatting —
    // it's culture-*ambiguous* input silently reading as a different number. .NET accepts group separators
    // without validating their position, so NumberStyles.Number turns the "1,5" a comma-decimal-locale user
    // typed for one-and-a-half into 15, raises no issue, and reports the F&O write as a success. There is no
    // safe guess available here (the builder cannot know the user's locale, and guessing would make the same
    // keystrokes mean different values for different users), so separators are rejected by construction.
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    // As DecimalStyles plus the exponent floating-point literals need ("1e3"); notably NOT AllowThousands.
    private const NumberStyles FloatStyles = NumberStyles.Float;

    // ISO 8601 only. The "K" specifier matches an empty string, "Z", or "+/-HH:mm", so each format covers
    // both the offset-bearing and offset-less spelling. A free DateTimeOffset.TryParse instead accepts
    // locale short-dates: "11/08/2026" is a *valid* InvariantCulture parse (8 November), so an NZ user who
    // meant 11 August saw no error — just a different date on the record.
    private static readonly string[] DateTimeOffsetFormats =
    {
        "yyyy-MM-ddK",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    };

    // AssumeUniversal: an input without an offset is UTC, never the host's local time — otherwise the same
    // keystrokes write a different instant from every machine, which is not something the user can see or
    // control from the payload builder. AdjustToUniversal: canonicalise to UTC so what is sent is what was
    // parsed, independent of the machine that parsed it.
    private const DateTimeStyles DateTimeOffsetStyles =
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <param name="blankIncludedMeansNull">
    /// How to read an explicitly-included field whose value is blank. <c>false</c> (the default) omits it —
    /// the right reading for a POST, where the service applies its own default. <c>true</c> reads it as
    /// "clear this field": the property is emitted as JSON <c>null</c> when it is nullable, and an issue is
    /// raised when it isn't. Callers building a PATCH body pass <c>true</c>, because omission and clearing
    /// are different requests there and the omit-everything reading produced a body of <c>{}</c> — which
    /// F&amp;O answers 204 to, so a user who emptied a field and saw a green badge believed it was cleared
    /// when nothing had been written.
    /// </param>
    public static ODataPayloadBuildResult BuildPayloadJson(
        ODataEntity entity,
        IEnumerable<ODataFieldValue> fieldValues,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? enumMembersByType = null,
        IReadOnlyDictionary<string, string>? defaultValues = null,
        bool enforceMandatory = true,
        bool blankIncludedMeansNull = false)
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
                    continue;
                }

                // Only a field the caller explicitly included can mean "clear me" — a property that isn't in
                // fieldValues at all was never asked about (it reaches here only via prop.Mandatory), so it
                // stays omitted rather than being nulled on the caller's behalf.
                if (!blankIncludedMeansNull || !inputExists || !input!.Include)
                {
                    continue; // optional blank omitted
                }

                if (!prop.Nullable)
                {
                    issues.Add($"Field '{prop.Name}' is included but blank — it isn't nullable; enter a value or exclude it.");
                    continue;
                }

                obj[prop.Name] = null;
                continue;
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

            // Its own case rather than folded into Edm.Int32: validated as an int, "40000" passed here and
            // was rejected by the service instead — a much worse place to find out about the range.
            case "Edm.Int16":
                if (short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i16))
                {
                    node = i16;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a 16-bit integer (-32768 to 32767).";
                return false;

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
                if (decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out var d))
                {
                    node = d;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a decimal number (no thousands separators; '.' as the decimal point).";
                return false;

            case "Edm.Double":
                if (double.TryParse(value, FloatStyles, CultureInfo.InvariantCulture, out var dbl))
                {
                    node = dbl;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a double (no thousands separators; '.' as the decimal point).";
                return false;

            case "Edm.Single":
                if (float.TryParse(value, FloatStyles, CultureInfo.InvariantCulture, out var f))
                {
                    node = f;
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a float (no thousands separators; '.' as the decimal point).";
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

            // Exact, not DateOnly.TryParse: even under InvariantCulture that is a *free* parse which read
            // "11/08/2026" as 8 November and "1,5" as 5 January of the current year. Latent while this
            // branch was unreachable from the app; live the moment Edm.Date started reaching it, so the
            // parse is pinned to the one format the issue message promises.
            case "Edm.Date":
                if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    node = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected a date (yyyy-MM-dd).";
                return false;

            case "Edm.DateTimeOffset":
                if (DateTimeOffset.TryParseExact(value, DateTimeOffsetFormats, CultureInfo.InvariantCulture, DateTimeOffsetStyles, out var dto))
                {
                    node = dto.ToString("O", CultureInfo.InvariantCulture);
                    issue = string.Empty;
                    return true;
                }
                issue = "Expected ISO 8601 (e.g. 2026-08-12 or 2026-08-12T14:30:00Z).";
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
