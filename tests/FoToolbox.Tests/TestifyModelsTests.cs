using DualWriteMapBrowserPlugin;
using Xunit;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class TestifyModelsTests
{
    [Fact]
    public void TestifyMapPlan_PreservesCoverageGaps()
    {
        var coverageGaps = new[]
        {
            new TestifyEnumCoverageGap("Status", "Closed"),
            new TestifyEnumCoverageGap("Status", "Canceled")
        };

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
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
            warnings: Array.Empty<string>(),
            coverageGaps: coverageGaps,
            blockingIssues: new[] { "Enum coverage missing for field 'Status'." });

        Assert.Equal(2, plan.CoverageGaps.Count);
        Assert.Equal("Status", plan.CoverageGaps[0].FieldName);
        Assert.Equal("Closed", plan.CoverageGaps[0].EnumValue);
        Assert.Equal("Status=Closed", plan.CoverageGaps[0].Detail);
        Assert.Equal("Status: Canceled, Closed", plan.CoverageGapFieldDetail);
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void TestifyMapPlan_AllowsRun_WhenPartialCoverageIsConfigured()
    {
        var configuration = new TestifyMapConfiguration
        {
            AllowPartialEnumCoverage = true
        };

        var plan = new TestifyMapPlan(
            mapId: "map-1",
            mapDisplayName: "Customers",
            foEntity: "CustomersV3",
            foEntityDetails: new FoToolbox.Core.OData.ODataEntity(
                "CustomersV3",
                Array.Empty<FoToolbox.Core.OData.ODataProperty>(),
                Array.Empty<FoToolbox.Core.OData.ODataNavigationProperty>()),
            configuration: configuration,
            foFilter: string.Empty,
            ceLegs: Array.Empty<TestifyLegPlan>(),
            createValues: new Dictionary<string, string>(),
            createPayloadJson: "{ }",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
            warnings: Array.Empty<string>(),
            coverageGaps: new[] { new TestifyEnumCoverageGap("Status", "Closed") },
            blockingIssues: Array.Empty<string>());

        Assert.True(plan.CanRun);
    }

    [Fact]
    public void TestifyMapPlan_BlocksRun_WhenCorrelatedCeLegsHaveNoAssertableCoverage()
    {
        var plan = new TestifyMapPlan(
            mapId: "map-1",
            mapDisplayName: "Customers",
            foEntity: "CustomersV3",
            foEntityDetails: new FoToolbox.Core.OData.ODataEntity(
                "CustomersV3",
                Array.Empty<FoToolbox.Core.OData.ODataProperty>(),
                Array.Empty<FoToolbox.Core.OData.ODataNavigationProperty>()),
            configuration: new TestifyMapConfiguration(),
            foFilter: string.Empty,
            ceLegs: new[]
            {
                new TestifyLegPlan("leg-1", "accounts", "$filter=name eq 'TESTIFY-ROW'", "Name", "name")
            },
            createValues: new Dictionary<string, string>(),
            createPayloadJson: "{ }",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
            warnings: new[]
            {
                "Skipped CE assertion for 'CreatedDateTime->createdon' on leg 'leg-1' because FO type 'Edm.DateTimeOffset' is not yet supported for direct CE assertions."
            },
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());

        Assert.False(plan.CanRun);
    }

    [Fact]
    public void TestifyMapPlan_PartialCoverageDoesNotBlockMissingEntityClassification()
    {
        var configuration = new TestifyMapConfiguration
        {
            AllowPartialEnumCoverage = true
        };

        var plan = new TestifyMapPlan(
            mapId: "map-1",
            mapDisplayName: "Customers",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: configuration,
            foFilter: string.Empty,
            ceLegs: Array.Empty<TestifyLegPlan>(),
            createValues: new Dictionary<string, string>(),
            createPayloadJson: string.Empty,
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
            warnings: Array.Empty<string>(),
            coverageGaps: new[] { new TestifyEnumCoverageGap("Status", "Closed") },
            blockingIssues: new[] { "FO entity 'CustomersV3' was not found in metadata." });

        Assert.False(plan.CanRun);
        Assert.Null(plan.FoEntityDetails);
        Assert.True(plan.Configuration.AllowPartialEnumCoverage);
    }

    [Fact]
    public void TestifyPreflightRow_PreservesBlockedCoverageStatusAndDetail()
    {
        var row = new TestifyPreflightRow(
            mapDisplayName: "Customers",
            mapId: "map-1",
            foEntity: "CustomersV3",
            enumFields: 1,
            plannedUpdates: 0,
            isReady: false,
            status: "Blocked: incomplete coverage",
            blockingIssue: "Unmapped enum members for field 'Status': 'Canceled', 'Closed'.",
            coverageGaps: new[]
            {
                new TestifyEnumCoverageGap("Status", "Closed"),
                new TestifyEnumCoverageGap("Status", "Canceled")
            });

        Assert.False(row.IsReady);
        Assert.Equal("Blocked: incomplete coverage", row.Status);
        Assert.Contains("Unmapped enum members for field 'Status'", row.BlockingIssue);
        Assert.Contains("'Closed'", row.BlockingIssue);
        Assert.Contains("'Canceled'", row.BlockingIssue);
        Assert.Equal("Status: Canceled, Closed", row.CoverageGapFieldDetail);
        Assert.Collection(
            row.CoverageGapsByField,
            gap =>
            {
                Assert.Equal("Status", gap.FieldName);
                Assert.Equal(new[] { "Canceled", "Closed" }, gap.EnumValues);
            });
    }

    [Fact]
    public void TestifyPreflightRow_PreservesBlockedMissingEntityStatus()
    {
        var row = new TestifyPreflightRow(
            mapDisplayName: "Customers",
            mapId: "map-1",
            foEntity: "CustomersV3",
            enumFields: 0,
            plannedUpdates: 0,
            isReady: false,
            status: "Blocked: missing entity",
            blockingIssue: "FO entity 'CustomersV3' was not found in metadata.",
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>());

        Assert.False(row.IsReady);
        Assert.Equal("Blocked: missing entity", row.Status);
        Assert.Equal("FO entity 'CustomersV3' was not found in metadata.", row.BlockingIssue);
        Assert.Empty(row.CoverageGaps);
    }

    [Fact]
    public void TestifyResultRow_ExposesPerFieldCoverageGapDetailForUiBinding()
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
                new TestifyEnumCoverageGap("Type", "Vendor"),
                new TestifyEnumCoverageGap("Status", "Canceled"),
                new TestifyEnumCoverageGap("Status", "Closed")
            });

        Assert.Equal("Type=Vendor; Status=Canceled; Status=Closed", row.CoverageGapDetail);
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
    public void TestifyResultRow_PreservesCoverageGapsByFieldForUiBinding()
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

        Assert.Collection(
            row.CoverageGapsByField,
            gap =>
            {
                Assert.Equal("Status", gap.FieldName);
                Assert.Equal(new[] { "Canceled", "Closed" }, gap.EnumValues);
                Assert.Equal("Status: Canceled, Closed", gap.Detail);
            },
            gap =>
            {
                Assert.Equal("Type", gap.FieldName);
                Assert.Equal(new[] { "Vendor" }, gap.EnumValues);
                Assert.Equal("Type: Vendor", gap.Detail);
            });
    }

    [Fact]
    public void TestifyEnumFieldPlan_FormatsEachMissingMemberForPrepareOutput()
    {
        var plan = new TestifyEnumFieldPlan(
            fieldName: "Status",
            enumType: "Contoso.StatusEnum",
            enumMembers: new[] { "Open", "Closed", "Canceled" },
            transformKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Open" },
            missingMembers: new[] { "Closed", "Canceled" },
            fixedValue: null,
            parseFailed: false,
            parseError: string.Empty);

        Assert.True(plan.HasCoverageGap);
        Assert.Equal("Unmapped enum members for field 'Status': 'Canceled', 'Closed'.", plan.CoverageGapDetail);
    }

    [Fact]
    public void TestifyEnumFieldPlan_HasEmptyCoverageGapDetail_WhenFullyMapped()
    {
        var plan = new TestifyEnumFieldPlan(
            fieldName: "Status",
            enumType: "Contoso.StatusEnum",
            enumMembers: new[] { "Open" },
            transformKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Open" },
            missingMembers: Array.Empty<string>(),
            fixedValue: null,
            parseFailed: false,
            parseError: string.Empty);

        Assert.False(plan.HasCoverageGap);
        Assert.Equal(string.Empty, plan.CoverageGapDetail);
    }
}
