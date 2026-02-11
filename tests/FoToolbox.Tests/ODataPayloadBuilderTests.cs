using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace FoToolbox.Tests;

public sealed class ODataPayloadBuilderTests
{
    [Fact]
    public void BuildPayloadJson_Builds_Typed_Json_And_Validates_Enums()
    {
        var entity = new ODataEntity(
            "TestEntity",
            new[]
            {
                new ODataProperty("Name", "Edm.String", Nullable: false, IsKey: false, IsMandatory: true),
                new ODataProperty("Count", "Edm.Int32", Nullable: true, IsKey: false, IsMandatory: false),
                new ODataProperty("IsActive", "Edm.Boolean", Nullable: false, IsKey: false, IsMandatory: false),
                new ODataProperty("Category", "My.EnumType", Nullable: false, IsKey: false, IsMandatory: false),
                new ODataProperty("OptionalNote", "Edm.String", Nullable: true, IsKey: false, IsMandatory: false),
            },
            Array.Empty<ODataNavigationProperty>());

        var enums = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["My.EnumType"] = new[] { "A", "B" }
        };

        var fields = new[]
        {
            new ODataFieldValue("Name", Include: true, Value: "hello"),
            new ODataFieldValue("Count", Include: true, Value: "42"),
            new ODataFieldValue("IsActive", Include: true, Value: "true"),
            new ODataFieldValue("Category", Include: true, Value: "B"),
            new ODataFieldValue("OptionalNote", Include: true, Value: "null"),
        };

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, fields, enums);
        Assert.True(result.Ok);
        Assert.NotNull(result.Json);

        using var doc = JsonDocument.Parse(result.Json!);
        var root = doc.RootElement;
        Assert.Equal("hello", root.GetProperty("Name").GetString());
        Assert.Equal(42, root.GetProperty("Count").GetInt32());
        Assert.True(root.GetProperty("IsActive").GetBoolean());
        Assert.Equal("B", root.GetProperty("Category").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("OptionalNote").ValueKind);
    }

    [Fact]
    public void BuildPayloadJson_Fails_When_Mandatory_Missing()
    {
        var entity = new ODataEntity(
            "TestEntity",
            new[]
            {
                new ODataProperty("Name", "Edm.String", Nullable: false, IsKey: false, IsMandatory: true),
            },
            Array.Empty<ODataNavigationProperty>());

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, Array.Empty<ODataFieldValue>(), enforceMandatory: true);
        Assert.False(result.Ok);
        Assert.Contains(result.Issues, i => i.Contains("mandatory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPayloadJson_Omits_Optional_Blanks()
    {
        var entity = new ODataEntity(
            "TestEntity",
            new[]
            {
                new ODataProperty("Name", "Edm.String", Nullable: false, IsKey: false, IsMandatory: true),
                new ODataProperty("Optional", "Edm.String", Nullable: true, IsKey: false, IsMandatory: false),
            },
            Array.Empty<ODataNavigationProperty>());

        var fields = new[]
        {
            new ODataFieldValue("Name", Include: true, Value: "x"),
            new ODataFieldValue("Optional", Include: true, Value: "  "),
        };

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, fields);
        Assert.True(result.Ok);

        using var doc = JsonDocument.Parse(result.Json!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("Name", out _));
        Assert.False(root.TryGetProperty("Optional", out _));
    }
}
