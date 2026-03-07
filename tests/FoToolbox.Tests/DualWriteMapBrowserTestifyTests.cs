using DualWriteMapBrowserPlugin;
using FoToolbox.Core.OData;
using Xunit;

namespace FoToolbox.Tests;

public sealed class DualWriteMapBrowserTestifyTests
{
    [Fact]
    public void EndToEnd_PlannerAndRunner_BuildsCreateAndPatchArtifacts()
    {
        var entity = new ODataEntity(
            "CustomersV3",
            new[]
            {
                new ODataProperty("AccountNumber", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true, MaxLength: "20"),
                new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true, MaxLength: "4"),
                new ODataProperty("CustomerType", "Default.CustomerType", Nullable: false, IsMandatory: false),
            },
            Array.Empty<ODataNavigationProperty>());

        var rawValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACCOUNTNUMBER"] = "CUST-0001",
            ["DATAAREAID"] = "USMF"
        };

        var normalized = TestifyPlanner.NormalizeMapProperties(rawValues, entity.Properties, out var warnings);
        Assert.Empty(warnings);

        var enumFields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomerType"] = new[] { "Retail", "Wholesale", "Online" }
        };

        var steps = TestifyPlanner.BuildMinimalPatchSteps(enumFields);
        Assert.Equal(2, steps.Count);

        normalized["CustomerType"] = "Retail";

        var enumByType = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default.CustomerType"] = new[] { "Retail", "Wholesale", "Online" }
        };

        var okPayload = TestifyRunner.TryBuildPayload(entity, normalized, enumByType, enforceMandatory: true, out var createJson, out var issues);
        Assert.True(okPayload, string.Join(" | ", issues));
        Assert.Contains("CustomerType", createJson);

        var okUrl = TestifyRunner.TryBuildEntityInstanceUrl(
            "https://contoso.operations.dynamics.com/data/CustomersV3",
            entity,
            normalized,
            out var instanceUrl,
            out var urlError);

        Assert.True(okUrl, urlError);
        Assert.Contains("AccountNumber='CUST-0001'", instanceUrl);
        Assert.Contains("dataAreaId='USMF'", instanceUrl);
        Assert.Contains("cross-company=true", instanceUrl);
    }
}
