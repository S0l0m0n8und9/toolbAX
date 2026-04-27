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
            warnings: Array.Empty<string>(),
            coverageGaps: new[] { new TestifyEnumCoverageGap("Status", "Closed") },
            blockingIssues: new[] { "Some other blocking issue." });

        var status = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("GetBlockedStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { plan });

        Assert.Equal("Blocked", status);
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
            patchesPlanned: 0,
            correlatedCeVerificationSucceeded: true);

        Assert.True(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsTrue_WhenAllPatchesCompleted()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 3,
            patchesPlanned: 3,
            correlatedCeVerificationSucceeded: true);

        Assert.True(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsFalse_WhenPatchSequenceStopsEarly()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 1,
            patchesPlanned: 2,
            correlatedCeVerificationSucceeded: true);

        Assert.False(succeeded);
    }

    [Fact]
    public void DidCeVerificationSucceedForCompletedRun_ReturnsFalse_WhenCorrelationCheckDidNotSucceed()
    {
        var succeeded = DualWriteMapBrowserViewModel.DidCeVerificationSucceedForCompletedRun(
            createSucceeded: true,
            patchesSucceeded: 2,
            patchesPlanned: 2,
            correlatedCeVerificationSucceeded: false);

        Assert.False(succeeded);
    }

    [Fact]
    public void TestifyResultRow_PreservesFailedCeVerification_ForCompletedPatchRun()
    {
        var row = new TestifyResultRow(
            mapDisplayName: "Customers",
            mapId: "map-1",
            valid: true,
            createSucceeded: true,
            patchesPlanned: 2,
            patchesSucceeded: 2,
            ceVerificationSucceeded: false,
            status: "Failed: CE correlation drift",
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>());

        Assert.False(row.CeVerificationSucceeded);
        Assert.Equal("Failed: CE correlation drift", row.Status);
    }

    [Fact]
    public void BuildCorrelationFilter_AddsCorrelationClause_WhenNoExistingFilterExists()
    {
        var filter = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("BuildCorrelationFilter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { string.Empty, "name", "TESTIFY-001" });

        Assert.Equal("name eq 'TESTIFY-001'", filter);
    }

    [Fact]
    public void BuildCorrelationFilter_PreservesExistingFilter_AndEscapesSingleQuotes()
    {
        var filter = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("BuildCorrelationFilter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { "statecode eq 0", "name", "O'Brien" });

        Assert.Equal("(statecode eq 0) and (name eq 'O''Brien')", filter);
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
}
