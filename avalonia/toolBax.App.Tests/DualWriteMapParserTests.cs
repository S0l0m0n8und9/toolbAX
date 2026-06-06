using System.Linq;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="DualWriteMapParser"/> reshapes a Dataverse <c>msdyn_dualwriteentitymaps</c>
/// response (the core read path of the Map Browser port) into <c>DwMapRecord</c>s — including the
/// FormattedValue option-set fallbacks and the nested <c>msdyn_mapping</c> / <c>msdyn_properties</c>
/// JSON. Pure logic, no UI / network → runs on Linux CI.
/// </summary>
public class DualWriteMapParserTests
{
    // A representative single-record response with formatted values + a two-leg-ish mapping document.
    private const string SampleResponse = """
    {
      "@odata.nextLink": "https://x.crm.dynamics.com/api/data/v9.2/msdyn_dualwriteentitymaps?$skiptoken=p2",
      "value": [
        {
          "msdyn_dualwriteentitymapid": "11111111-1111-1111-1111-111111111111",
          "solutionid": "22222222-2222-2222-2222-222222222222",
          "msdyn_name": "vendtable_account",
          "msdyn_displayname": "Vendors V2 to Accounts",
          "msdyn_version": "1.0.0.8",
          "createdon": "2024-01-02T03:04:05Z",
          "modifiedon": "2024-05-06T07:08:09Z",
          "statecode": 0,
          "statecode@OData.Community.Display.V1.FormattedValue": "Active",
          "statuscode": 1,
          "statuscode@OData.Community.Display.V1.FormattedValue": "Published",
          "_ownerid_value": "33333333-3333-3333-3333-333333333333",
          "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Dual Write Service",
          "msdyn_mapping": "{\"id\":\"map-1\",\"description\":\"vendor map\",\"legs\":[{\"id\":\"leg-1\",\"sourceSchema\":\"VendVendorV2Entity\",\"sourceSchemaDistinctName\":\"VendVendorV2Entity (Distinct)\",\"destinationSchema\":\"accounts\",\"sourceEnvironmentType\":\"AX\",\"destinationEnvironmentType\":\"CRM\",\"sourceFilter\":\"VendGroup == 'DOM'\",\"reversedSourceFilter\":\"accounttype eq 'vendor'\",\"fieldMappings\":[{\"id\":\"fm-1\",\"sourceField\":\"AccountNumber\",\"destinationField\":\"accountnumber\",\"syncDirection\":\"Bidirectional\",\"destinationLookupFieldRelatedEntity\":null,\"isSystemGenerated\":false,\"valueTransforms\":[{\"sourceField\":\"CustGroup\",\"destinationField\":\"cdm_customergroup\",\"transformType\":\"ValueMap\",\"defaultValue\":\"DOM\",\"createValuesOnDestination\":false,\"valueMap\":{\"10\":\"Wholesale\",\"20\":\"Retail\"}}]},{\"id\":\"fm-2\",\"sourceField\":\"CurrencyCode\",\"destinationField\":\"transactioncurrencyid\",\"syncDirection\":\"Forward\",\"destinationLookupFieldRelatedEntity\":\"transactioncurrency\",\"isSystemGenerated\":true,\"valueTransforms\":[]}]}]}",
          "msdyn_properties": "{\"IntegrationKey\":\"AccountNumber\",\"IsActive\":true}"
        }
      ]
    }
    """;

    [Fact]
    public void MapsPath_targets_the_dualwrite_map_entity_set_with_select_and_orderby()
    {
        var path = DualWriteMapParser.MapsPath();

        Assert.StartsWith("msdyn_dualwriteentitymaps?", path);
        Assert.Contains("$select=", path);
        Assert.Contains("msdyn_dualwriteentitymapid", path);
        Assert.Contains("msdyn_mapping", path);
        Assert.Contains("msdyn_properties", path);
        Assert.Contains("$orderby=modifiedon", path);
    }

    [Fact]
    public void ParsePage_reads_core_columns()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();

        Assert.Equal("11111111-1111-1111-1111-111111111111", record.Id);
        Assert.Equal("22222222-2222-2222-2222-222222222222", record.SolutionId);
        Assert.Equal("vendtable_account", record.Name);
        Assert.Equal("Vendors V2 to Accounts", record.DisplayName);
        Assert.Equal("1.0.0.8", record.Version);
        Assert.Equal("Vendors V2 to Accounts", record.Title);
    }

    [Fact]
    public void ParsePage_prefers_formatted_values_for_optionsets_and_lookups()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();

        Assert.Equal("Active", record.State);
        Assert.Equal("Published", record.Status);
        Assert.Equal("Dual Write Service", record.Owner);
    }

    [Fact]
    public void ParsePage_parses_dates_as_utc()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();

        Assert.NotNull(record.ModifiedOn);
        Assert.Equal(2024, record.ModifiedOn!.Value.Year);
        Assert.Equal(System.TimeSpan.Zero, record.ModifiedOn.Value.Offset);
        Assert.NotNull(record.CreatedOn);
    }

    [Fact]
    public void ParsePage_parses_mapping_legs()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();
        var leg = Assert.Single(record.Legs);

        Assert.Equal("leg-1", leg.LegId);
        Assert.Equal("VendVendorV2Entity", leg.SourceSchema);
        Assert.Equal("accounts", leg.DestinationSchema);
        Assert.Equal("AX", leg.SourceEnvironmentType);
        Assert.Equal("CRM", leg.DestinationEnvironmentType);
        Assert.Equal("VendGroup == 'DOM'", leg.SourceFilter);
        Assert.Equal("accounttype eq 'vendor'", leg.ReversedSourceFilter);
        Assert.Equal(2, leg.FieldMappings);
    }

    [Fact]
    public void ParsePage_flattens_field_mappings_with_sync_direction_and_lookup()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();

        Assert.Equal(2, record.Fields.Count);
        var keyField = record.Fields.Single(f => f.SourceField == "AccountNumber");
        Assert.Equal("accountnumber", keyField.DestinationField);
        Assert.Equal("Bidirectional", keyField.SyncDirection);
        Assert.Equal("leg-1", keyField.LegId);
        Assert.Equal(1, keyField.ValueTransforms);
        Assert.False(keyField.IsSystemGenerated);

        var currency = record.Fields.Single(f => f.SourceField == "CurrencyCode");
        Assert.Equal("transactioncurrency", currency.DestinationLookupEntity);
        Assert.True(currency.HasLookup);
        Assert.True(currency.IsSystemGenerated);
    }

    [Fact]
    public void ParsePage_flattens_value_transforms_with_serialized_value_map()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();
        var transform = Assert.Single(record.ValueTransforms);

        Assert.Equal("ValueMap", transform.TransformType);
        Assert.Equal("DOM", transform.DefaultValue);
        Assert.True(transform.HasDefaultValue);
        Assert.False(transform.CreateValuesOnDestination);
        Assert.Contains("Wholesale", transform.ValueMap);
        Assert.True(transform.HasValueMap);
    }

    [Fact]
    public void ParsePage_flattens_properties()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();

        var key = record.Properties.Single(p => p.Key == "IntegrationKey");
        Assert.Equal("AccountNumber", key.Value);
        Assert.Contains(record.Properties, p => p.Key == "IsActive" && p.Value == "true");
    }

    [Fact]
    public void ParsePage_returns_the_next_link()
    {
        var page = DualWriteMapParser.ParsePage(SampleResponse);

        Assert.Equal(
            "https://x.crm.dynamics.com/api/data/v9.2/msdyn_dualwriteentitymaps?$skiptoken=p2",
            page.NextLink);
    }

    [Fact]
    public void ParsePage_with_no_more_pages_has_a_null_next_link()
    {
        var page = DualWriteMapParser.ParsePage("{\"value\":[]}");

        Assert.Empty(page.Records);
        Assert.Null(page.NextLink);
    }

    [Fact]
    public void ParsePage_tolerates_malformed_mapping_json()
    {
        const string json = """
        { "value": [ { "msdyn_dualwriteentitymapid": "a", "msdyn_name": "n", "msdyn_mapping": "not json", "msdyn_properties": "" } ] }
        """;

        var record = DualWriteMapParser.ParsePage(json).Records.Single();

        Assert.Equal("n", record.Name);
        Assert.Empty(record.Legs);
        Assert.Empty(record.Fields);
        Assert.Empty(record.Properties);
    }

    [Fact]
    public void ParsePage_tolerates_empty_or_null_input()
    {
        Assert.Empty(DualWriteMapParser.ParsePage(null).Records);
        Assert.Empty(DualWriteMapParser.ParsePage("").Records);
        Assert.Empty(DualWriteMapParser.ParsePage("not json at all").Records);
    }
}
