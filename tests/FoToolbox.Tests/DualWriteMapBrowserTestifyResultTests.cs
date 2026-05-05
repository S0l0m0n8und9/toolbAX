using DualWriteMapBrowserPlugin;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class DualWriteMapBrowserTestifyResultTests
{
    [Fact]
    public void GetBlockedStatus_ReturnsIncompleteCoverage_WhenCoverageIsRequired()
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
            ceLegs: Array.Empty<TestifyLegPlan>(),
            createValues: new Dictionary<string, string>(),
            createPayloadJson: string.Empty,
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
            warnings: Array.Empty<string>(),
            coverageGaps: new[] { new TestifyEnumCoverageGap("Status", "Closed") },
            blockingIssues: new[] { "Enum coverage missing for field 'Status'." });

        var status = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("GetBlockedStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { plan });

        Assert.Equal("Blocked: incomplete coverage", status);
    }

    [Fact]
    public void GetBlockedStatus_ReturnsGenericBlocked_WhenPartialCoverageIsAllowed()
    {
        var plan = new TestifyMapPlan(
            mapId: "map-1",
            mapDisplayName: "Customers",
            foEntity: "CustomersV3",
            foEntityDetails: new FoToolbox.Core.OData.ODataEntity(
                "CustomersV3",
                Array.Empty<FoToolbox.Core.OData.ODataProperty>(),
                Array.Empty<FoToolbox.Core.OData.ODataNavigationProperty>()),
            configuration: new TestifyMapConfiguration
            {
                AllowPartialEnumCoverage = true
            },
            foFilter: string.Empty,
            ceLegs: Array.Empty<TestifyLegPlan>(),
            createValues: new Dictionary<string, string>(),
            createPayloadJson: "{ }",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
            warnings: Array.Empty<string>(),
            coverageGaps: new[] { new TestifyEnumCoverageGap("Status", "Closed") },
            blockingIssues: new[] { "Some other blocking issue." });

        var status = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("GetBlockedStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { plan });

        Assert.Equal("Blocked", status);
    }

    [Fact]
    public void GetBlockedStatus_ReturnsNoAssertableCoverage_WhenCeLegsExistButNoAssertions()
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

        var status = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("GetBlockedStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { plan });

        Assert.Equal("Blocked: no assertable CE coverage", status);
    }

    [Fact]
    public void FormatBlockingIssue_IncludesSkippedAssertionReason_WhenNoAssertableCoverageExists()
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
            blockingIssues: new[]
            {
                "No assertable CE field coverage could be generated for runnable AX->CRM legs. Skipped CE assertion for 'CreatedDateTime->createdon' on leg 'leg-1' because FO type 'Edm.DateTimeOffset' is not yet supported for direct CE assertions."
            });

        var blockingIssue = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("FormatBlockingIssue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { plan });

        Assert.Contains("No assertable CE field coverage could be generated", (string)blockingIssue!);
        Assert.Contains("CreatedDateTime->createdon", (string)blockingIssue!);
    }

    [Fact]
    public void CoverageGapDetail_ListsEachFieldAndEnumValue()
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
                new TestifyEnumCoverageGap("Type", "Vendor")
            });

        Assert.Equal("Status=Closed; Type=Vendor", row.CoverageGapDetail);
        Assert.Equal("Status: Closed; Type: Vendor", row.CoverageGapFieldDetail);
        Assert.Collection(
            row.CoverageGaps,
            gap =>
            {
                Assert.Equal("Status", gap.FieldName);
                Assert.Equal("Closed", gap.EnumValue);
            },
            gap =>
            {
                Assert.Equal("Type", gap.FieldName);
                Assert.Equal("Vendor", gap.EnumValue);
            });
        Assert.Collection(
            row.CoverageGapsByField,
            gap =>
            {
                Assert.Equal("Status", gap.FieldName);
                Assert.Equal(new[] { "Closed" }, gap.EnumValues);
            },
            gap =>
            {
                Assert.Equal("Type", gap.FieldName);
                Assert.Equal(new[] { "Vendor" }, gap.EnumValues);
            });
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsTrue_ForCreateOnlyRun()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 0,
            patchesPlanned: 0);

        Assert.True(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsTrue_WhenAllPatchesCompleted()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 3,
            patchesPlanned: 3);

        Assert.True(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsFalse_WhenPatchSequenceStopsEarly()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 1,
            patchesPlanned: 2);

        Assert.False(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsFalse_WhenNoAssertionsWereEvaluated()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 0,
            patchesPlanned: 0,
            ceAssertionsEvaluated: 0);

        Assert.False(succeeded);
    }

    [Fact]
    public void CoverageGapDetail_ReturnsEmpty_WhenNoCoverageGapsExist()
    {
        var row = new TestifyResultRow(
            mapDisplayName: "Customers",
            mapId: "map-1",
            valid: false,
            createSucceeded: false,
            patchesPlanned: 0,
            patchesSucceeded: 0,
            ceVerificationSucceeded: false,
            status: "Blocked: missing entity",
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>());

        Assert.Equal(string.Empty, row.CoverageGapDetail);
        Assert.Empty(row.CoverageGaps);
    }

    [Fact]
    public void CeAssertionSummary_ReportsPassAndFailCounts()
    {
        var row = new TestifyResultRow(
            mapDisplayName: "Customers",
            mapId: "map-1",
            valid: false,
            createSucceeded: true,
            patchesPlanned: 1,
            patchesSucceeded: 1,
            ceVerificationSucceeded: false,
            status: "CE mismatch",
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            ceFieldAssertions: new[]
            {
                new TestifyCeFieldAssertion("Create", "leg-1", "Name", "name", "TEST", "TEST", passed: true),
                new TestifyCeFieldAssertion("Patch 1", "leg-1", "Status", "statuscode", "2", "1", passed: false)
            });

        Assert.Equal("1/2", row.CeAssertionSummary);
        Assert.Equal(2, row.CeAssertionsTotal);
        Assert.Equal(1, row.CeAssertionsPassed);
        Assert.Equal(1, row.CeAssertionsFailed);
        Assert.Equal("Create:name=pass; Patch 1:statuscode=fail", row.CeAssertionDetail);
    }
}
