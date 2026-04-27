using DualWriteMapBrowserPlugin;
using Xunit;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class TestifyPlannerTests
{
    [Fact]
    public void TestifyMapPlan_GroupsCoverageGapsByFieldForPrepareOutput()
    {
        var plan = new TestifyMapPlan(
            mapId: "map-1",
            mapDisplayName: "Customers",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: new TestifyMapConfiguration(),
            foFilter: string.Empty,
            ceLegs: Array.Empty<TestifyLegPlan>(),
            createValues: new Dictionary<string, string>(),
            createPayloadJson: string.Empty,
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = new TestifyEnumFieldPlan(
                    fieldName: "Status",
                    enumType: "Contoso.StatusEnum",
                    enumMembers: new[] { "Open", "Canceled", "Closed" },
                    transformKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Open" },
                    missingMembers: new[] { "Closed", "Canceled" },
                    fixedValue: null,
                    parseFailed: false,
                    parseError: string.Empty)
            },
            patchSteps: Array.Empty<TestifyPatchStep>(),
            warnings: Array.Empty<string>(),
            coverageGaps: new[]
            {
                new TestifyEnumCoverageGap("Status", "Closed"),
                new TestifyEnumCoverageGap("Status", "Canceled")
            },
            blockingIssues: new[] { "Enum coverage missing for field 'Status'." });

        Assert.Collection(
            plan.CoverageGapsByField,
            gap =>
            {
                Assert.Equal("Status", gap.FieldName);
                Assert.Equal(new[] { "Canceled", "Closed" }, gap.EnumValues);
                Assert.Equal("Status: Canceled, Closed", gap.Detail);
            });
        Assert.Equal("Status: Canceled, Closed", plan.CoverageGapFieldDetail);
        Assert.Equal("Unmapped enum members for field 'Status': 'Closed', 'Canceled'.", plan.EnumFields["Status"].CoverageGapDetail);
    }

    [Fact]
    public void TestifyResultRow_GroupsCoverageGapsByField()
    {
        var row = new TestifyResultRow(
            mapDisplayName: "Customers",
            mapId: "map-1",
            valid: false,
            createSucceeded: false,
            patchesPlanned: 0,
            patchesSucceeded: 0,
            ceVerificationSucceeded: false,
            status: "Blocked: incomplete coverage",
            coverageGaps: new[]
            {
                new TestifyEnumCoverageGap("Status", "Closed"),
                new TestifyEnumCoverageGap("Status", "Canceled"),
                new TestifyEnumCoverageGap("Type", "Vendor")
            });

        Assert.Equal("Status: Canceled, Closed; Type: Vendor", row.CoverageGapFieldDetail);
        Assert.Collection(
            row.CoverageGapsByField,
            gap =>
            {
                Assert.Equal("Status", gap.FieldName);
                Assert.Equal(new[] { "Canceled", "Closed" }, gap.EnumValues);
            },
            gap =>
            {
                Assert.Equal("Type", gap.FieldName);
                Assert.Equal(new[] { "Vendor" }, gap.EnumValues);
            });
    }

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
    public void ValidateFixedEnumCoverage_DoesNotBlockSingleMemberField()
    {
        var enumMembers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = new[] { "Open" }
        };
        var fixedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = "Open"
        };

        var issues = TestifyPlanner.ValidateFixedEnumCoverage(enumMembers, fixedValues);

        Assert.Empty(issues);
    }

    [Fact]
    public void ValidateFixedEnumCoverage_BlocksInvalidPinnedValue()
    {
        var enumMembers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = new[] { "Open", "Closed" }
        };
        var fixedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = "Pending"
        };

        var issues = TestifyPlanner.ValidateFixedEnumCoverage(enumMembers, fixedValues);

        Assert.Single(issues);
        Assert.Contains("is not valid for field 'Status'", issues[0]);
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
