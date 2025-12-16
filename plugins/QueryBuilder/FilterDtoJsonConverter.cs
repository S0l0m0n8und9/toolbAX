using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryBuilderPlugin;

internal sealed class FilterDtoJsonConverter : JsonConverter<FilterDto>
{
    public override FilterDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected an object for {nameof(FilterDto)} but got {root.ValueKind}.");
        }

        var hasChildren = root.TryGetProperty("Children", out _) || root.TryGetProperty("children", out _);
        var hasLogicalOperator = root.TryGetProperty("LogicalOperator", out _) || root.TryGetProperty("logicalOperator", out _);
        var hasField = root.TryGetProperty("Field", out _) || root.TryGetProperty("field", out _);

        if (hasChildren || (hasLogicalOperator && !hasField))
        {
            return JsonSerializer.Deserialize<FilterGroupDto>(root.GetRawText(), options);
        }

        return JsonSerializer.Deserialize<FilterConditionDto>(root.GetRawText(), options);
    }

    public override void Write(Utf8JsonWriter writer, FilterDto value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case FilterConditionDto cond:
                JsonSerializer.Serialize(writer, cond, options);
                break;
            case FilterGroupDto grp:
                JsonSerializer.Serialize(writer, grp, options);
                break;
            default:
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
                break;
        }
    }
}
