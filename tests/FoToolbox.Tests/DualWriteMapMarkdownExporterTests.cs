using DualWriteMapBrowserPlugin;
using System;
using Xunit;

namespace FoToolbox.Tests;

public sealed class DualWriteMapMarkdownExporterTests
{
    [Fact]
    public void Export_IncludesAllCurrentlyExposedMappingSections()
    {
        var record = new DualWriteMapRecord(
            id: "map-123",
            solutionId: "solution-456",
            name: "msdyn_customer_map",
            displayName: "Customer map",
            version: "1.0.0.0",
            state: "Active",
            status: "Live",
            owner: "Adele Vance",
            createdOn: DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            modifiedOn: DateTimeOffset.Parse("2025-01-02T12:34:56Z"),
            mappingRows: new[]
            {
                new JsonTableRow("$.legs[0].fieldMappings[0].sourceField", "string", "CustAccount")
            },
            mappingSummaryRows: new[]
            {
                new MappingSummaryRow("legs.count", "1")
            },
            mappingLegRows: new[]
            {
                new MappingLegRow("leg-1", "CustTable", "CustTableDistinct", "accounts", "FO", "Dataverse", "dataAreaId == 'usmf'", "accountnumber ne null", 1)
            },
            mappingFieldRows: new[]
            {
                new MappingFieldRow("leg-1", "CustTable", "accounts", "Bidirectional", "CustAccount", "accountnumber", "account", true, 1)
            },
            mappingValueTransformRows: new[]
            {
                new MappingValueTransformRow("leg-1", "Blocked", "statecode", "ValueMap", "0", true, "0=Active", true)
            },
            propertiesRows: new[]
            {
                new PropertyTableRow("isManaged", "boolean", "false")
            },
            mappingRaw: "{}",
            propertiesRaw: "{}");

        var markdown = DualWriteMapMarkdownExporter.Export(record);

        Assert.Contains("# Customer map", markdown);
        Assert.Contains("## Map Details", markdown);
        Assert.Contains("**Map ID:** map-123", markdown);
        Assert.Contains("## Mapping Data", markdown);
        Assert.Contains("| $.legs[0].fieldMappings[0].sourceField | string | CustAccount |", markdown);
        Assert.Contains("## Mapping Summary", markdown);
        Assert.Contains("| legs.count | 1 |", markdown);
        Assert.Contains("## Mapping Legs", markdown);
        Assert.Contains("| leg-1 | CustTable | CustTableDistinct | accounts | FO | Dataverse | dataAreaId == 'usmf' | accountnumber ne null | 1 |", markdown);
        Assert.Contains("## Mapping Fields", markdown);
        Assert.Contains("| leg-1 | Bidirectional | CustAccount | accountnumber | account | true | 1 | CustTable | accounts |", markdown);
        Assert.Contains("## Value Transforms", markdown);
        Assert.Contains("| leg-1 | Blocked | statecode | ValueMap | 0 | 0=Active | true |", markdown);
        Assert.Contains("## Properties", markdown);
        Assert.Contains("| isManaged | boolean | false |", markdown);
        Assert.Contains("## Raw Mapping JSON", markdown);
        Assert.Contains("```json\r\n{}\r\n```", markdown);
        Assert.Contains("## Raw Properties JSON", markdown);
    }

    [Fact]
    public void Export_RendersMissingOptionalFieldsPredictably()
    {
        var record = new DualWriteMapRecord(
            id: "map-optional",
            solutionId: string.Empty,
            name: string.Empty,
            displayName: string.Empty,
            version: string.Empty,
            state: string.Empty,
            status: string.Empty,
            owner: string.Empty,
            createdOn: null,
            modifiedOn: null,
            mappingRows: Array.Empty<JsonTableRow>(),
            mappingSummaryRows: Array.Empty<MappingSummaryRow>(),
            mappingLegRows: new[]
            {
                new MappingLegRow("", "", "", "", "", "", "", "", 0)
            },
            mappingFieldRows: new[]
            {
                new MappingFieldRow("", "", "", "", "", "", "", null, 0)
            },
            mappingValueTransformRows: new[]
            {
                new MappingValueTransformRow("", "", "", "", "", false, "", null)
            },
            propertiesRows: new[]
            {
                new PropertyTableRow("", "", "")
            },
            mappingRaw: null,
            propertiesRaw: null);

        var markdown = DualWriteMapMarkdownExporter.Export(record);

        Assert.Contains("# (not set)", markdown);
        Assert.Contains("**Solution ID:** (not set)", markdown);
        Assert.Contains("**Created:** (not set)", markdown);
        Assert.Contains("| (not set) | (not set) | (not set) |", markdown);
        Assert.Contains("| (not set) | (not set) | (not set) | (not set) | (not set) | (not set) | (not set) | (not set) | 0 |", markdown);
        Assert.Contains("| (not set) | (not set) | (not set) | (not set) | (not set) | (not set) | 0 | (not set) | (not set) |", markdown);
        Assert.Contains("| (not set) | (not set) | (not set) | (not set) | (not set) | (not set) | (not set) |", markdown);
        Assert.Contains("| (not set) | (not set) | (not set) |", markdown);
        Assert.Contains("```json\r\n(not set)\r\n```", markdown);
    }
}
