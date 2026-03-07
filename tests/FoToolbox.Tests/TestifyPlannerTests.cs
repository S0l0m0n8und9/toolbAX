using DualWriteMapBrowserPlugin;
using Xunit;

namespace FoToolbox.Tests;

public sealed class TestifyPlannerTests
{
    [Fact]
    public void BuildMinimalPatchSteps_UsesMaxCardinalityMinusOne()
    {
        var enumMembers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FieldA"] = new[] { "A", "B", "C" },
            ["FieldB"] = new[] { "X", "Y" }
        };

        var steps = TestifyPlanner.BuildMinimalPatchSteps(enumMembers);

        Assert.Equal(2, steps.Count);
        Assert.Equal("B", steps[0].EnumValues["FieldA"]);
        Assert.Equal("Y", steps[0].EnumValues["FieldB"]);
        Assert.Equal("C", steps[1].EnumValues["FieldA"]);
        Assert.Equal("Y", steps[1].EnumValues["FieldB"]);
    }

    [Fact]
    public void ExtractEqualityConstraints_ParsesStringEnumAndBoolean()
    {
        var filter = "(Status eq My.StatusEnum'Open') and (dataAreaId eq 'USMF') and (IsActive eq true)";

        var constraints = TestifyPlanner.ExtractEqualityConstraints(filter);

        Assert.Equal("Open", constraints["Status"]);
        Assert.Equal("USMF", constraints["dataAreaId"]);
        Assert.Equal("true", constraints["IsActive"]);
    }

    [Fact]
    public void ValidateFixedEnumCoverage_BlocksPinnedMultiMemberField()
    {
        var enumMembers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = new[] { "Open", "Closed" }
        };
        var fixedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = "Open"
        };

        var issues = TestifyPlanner.ValidateFixedEnumCoverage(enumMembers, fixedValues);

        Assert.Single(issues);
        Assert.Contains("preventing full enum coverage", issues[0]);
    }

    [Fact]
    public void ExtractMandatoryFieldLabels_ParsesInfologMessage()
    {
        const string body = "Write returned RecId 0. Infolog: Error: Mandatory field 'Organization name' not set.";
        var labels = TestifyPlanner.ExtractMandatoryFieldLabels(body);
        Assert.Single(labels);
        Assert.Equal("Organization name", labels[0]);
    }

    [Fact]
    public void ExtractLookupValidationIssues_ParsesFieldAndTable()
    {
        const string body = "Warning: The value 'TESTIFY202' in field 'Customer group' is not found in the related table 'Customer groups'.";

        var issues = TestifyPlanner.ExtractLookupValidationIssues(body);

        Assert.Single(issues);
        Assert.Equal("Customer group", issues[0].FieldLabel);
        Assert.Equal("Customer groups", issues[0].RelatedTable);
        Assert.Equal("TESTIFY202", issues[0].ProvidedValue);
    }

    [Fact]
    public void ResolveFieldByLabel_MatchesOrganizationName()
    {
        var properties = new[]
        {
            new FoToolbox.Core.OData.ODataProperty("CustomerAccount", "Edm.String", Nullable: false, IsKey: true),
            new FoToolbox.Core.OData.ODataProperty("OrganizationName", "Edm.String", Nullable: false, IsMandatory: false),
            new FoToolbox.Core.OData.ODataProperty("NameAlias", "Edm.String", Nullable: true, IsMandatory: false)
        };

        var currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomerAccount"] = "C-001"
        };

        var field = TestifyPlanner.ResolveFieldByLabel("Organization name", properties, currentValues);

        Assert.Equal("OrganizationName", field);
    }

    [Fact]
    public void ExtractMapPropertyCandidates_SkipsTransformValueMapsFromMappingJson()
    {
        const string mappingRaw = """
{
  "legs": [
    {
      "fieldMappings": [
        {
          "sourceField": "INVOICEADDRESS",
          "valueTransforms": [
            {
              "transformType": "ValueMap",
              "valueMap": {
                "invoiceAccount": "806380000",
                "orderAccount": "806380001"
              }
            }
          ]
        }
      ]
    }
  ],
  "version": "1"
}
""";

        const string propertiesRaw = """
{
  "AccountNum": "CUST-01",
  "dataAreaId": "USMF"
}
""";

        var values = TestifyPlanner.ExtractMapPropertyCandidates(mappingRaw, propertiesRaw);

        Assert.True(values.ContainsKey("AccountNum"));
        Assert.True(values.ContainsKey("dataAreaId"));
        Assert.False(values.ContainsKey("invoiceAccount"));
        Assert.False(values.ContainsKey("orderAccount"));
    }
}
