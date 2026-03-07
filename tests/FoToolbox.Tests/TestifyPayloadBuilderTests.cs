using DualWriteMapBrowserPlugin;
using FoToolbox.Core.OData;
using Xunit;

namespace FoToolbox.Tests;

public sealed class TestifyPayloadBuilderTests
{
    [Fact]
    public void NormalizeMapProperties_UsesFoMetadataCasing()
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACCOUNTNUMBER"] = "A-100",
            ["DATAAREAID"] = "USMF"
        };
        var properties = new[]
        {
            new ODataProperty("AccountNumber", "Edm.String", Nullable: false, IsKey: true),
            new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true),
        };

        var normalized = TestifyPlanner.NormalizeMapProperties(raw, properties, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal("A-100", normalized["AccountNumber"]);
        Assert.Equal("USMF", normalized["dataAreaId"]);
    }

    [Fact]
    public void TrimToMaxLength_TruncatesLongString()
    {
        var prop = new ODataProperty("Name", "Edm.String", Nullable: false, MaxLength: "5");
        var trimmed = TestifyPlanner.TrimToMaxLength(prop, "abcdefgh");
        Assert.Equal("abcde", trimmed);
    }

    [Fact]
    public void TryBuildPayload_FailsWhenMandatoryMissing()
    {
        var entity = new ODataEntity(
            "Customers",
            new[]
            {
                new ODataProperty("AccountNumber", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true),
                new ODataProperty("Name", "Edm.String", Nullable: false, IsMandatory: true)
            },
            Array.Empty<ODataNavigationProperty>());

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AccountNumber"] = "A-100"
        };

        var ok = TestifyRunner.TryBuildPayload(
            entity,
            values,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            enforceMandatory: true,
            out _,
            out var issues);

        Assert.False(ok);
        Assert.Contains(issues, i => i.Contains("mandatory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateDefaultValue_DataAreaIdWithoutDefault_ReturnsNull()
    {
        var property = new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true);
        var result = TestifyPlanner.GenerateDefaultValue(
            property,
            runToken: "TESTIFY",
            enumMembersByType: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            defaultCompany: null);

        Assert.Null(result);
    }

    [Fact]
    public void GenerateDefaultValue_DataAreaIdWithDefault_ReturnsDefaultCompany()
    {
        var property = new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true);
        var result = TestifyPlanner.GenerateDefaultValue(
            property,
            runToken: "TESTIFY",
            enumMembersByType: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            defaultCompany: "USMF");

        Assert.Equal("USMF", result);
    }
}
