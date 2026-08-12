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
    public void Leg_exposes_its_source_filter_as_odata()
    {
        var record = DualWriteMapParser.ParsePage(SampleResponse).Records.Single();
        var leg = Assert.Single(record.Legs);

        Assert.Equal("VendGroup == 'DOM'", leg.SourceFilter);
        Assert.Equal("VendGroup eq 'DOM'", leg.SourceFilterOData); // X++ == translated to OData eq
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
    public void CountPath_requests_a_top1_count()
    {
        Assert.Equal("accounts?$top=1&$count=true", DualWriteMapParser.CountPath("accounts", null));

        var filtered = DualWriteMapParser.CountPath("accounts", "accounttype eq 'vendor'");
        Assert.StartsWith("accounts?$top=1&$count=true&$filter=", filtered);
        Assert.Contains(System.Uri.EscapeDataString("accounttype eq 'vendor'"), filtered);
    }

    [Fact]
    public void FoCountPath_is_a_cross_company_data_count()
    {
        Assert.Equal("/data/Customers?$top=1&$count=true&cross-company=true",
            DualWriteMapParser.FoCountPath("Customers", null));

        var filtered = DualWriteMapParser.FoCountPath("Customers", "CustomerGroupId eq 'DOM'");
        Assert.StartsWith("/data/Customers?$top=1&$count=true&cross-company=true&$filter=", filtered);
        Assert.Contains(System.Uri.EscapeDataString("CustomerGroupId eq 'DOM'"), filtered);
    }

    [Theory]
    [InlineData("{\"@odata.count\":42,\"value\":[]}", 42L)]
    [InlineData("{\"@odata.count\":\"7\",\"value\":[]}", 7L)]
    public void ParseCount_reads_the_odata_count(string json, long expected) =>
        Assert.Equal(expected, DualWriteMapParser.ParseCount(json)!.Count);

    [Theory]
    [InlineData("{\"value\":[]}")]
    [InlineData("not json")]
    [InlineData(null)]
    public void ParseCount_is_null_when_absent_or_unparseable(string? json) =>
        Assert.Null(DualWriteMapParser.ParseCount(json));

    // --- #159: the Dataverse 5,000-row count cap has to be visible, not silently reported as a total ---

    [Fact]
    public void CountAnnotations_names_the_documented_dataverse_cap_annotations()
    {
        // These are the two annotation names the Web API docs say to request via Prefer alongside
        // $count=true; CoreDataverseClient sends this list verbatim.
        Assert.Equal(
            "Microsoft.Dynamics.CRM.totalrecordcount,Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded",
            DualWriteMapParser.CountAnnotations);
        Assert.Equal(5000, DualWriteMapParser.DataverseStandardCountCap);
    }

    [Fact]
    public void ParseCount_flags_a_capped_count_from_the_cap_annotation()
    {
        // What a 42,000-row table actually returns: the count is the 5,000 ceiling, and only the
        // annotation says so.
        const string json = """
        {"@odata.count":5000,"@Microsoft.Dynamics.CRM.totalrecordcount":5000,
         "@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded":true,"value":[]}
        """;

        var count = DualWriteMapParser.ParseCount(json);

        Assert.Equal(5000, count!.Count);
        Assert.True(count.CapExceeded);
        Assert.True(count.IsCappedAt(DualWriteMapParser.DataverseStandardCountCap));
    }

    [Fact]
    public void ParseCount_trusts_a_negative_cap_annotation_at_exactly_the_cap()
    {
        // A table with exactly 5,000 rows: same number, annotation says the limit was NOT exceeded, so
        // this is a real total and the count-cap heuristic must not second-guess it.
        const string json = """
        {"@odata.count":5000,"@Microsoft.Dynamics.CRM.totalrecordcount":5000,
         "@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded":false,"value":[]}
        """;

        var count = DualWriteMapParser.ParseCount(json);

        Assert.False(count!.CapExceeded);
        Assert.False(count.IsCappedAt(DualWriteMapParser.DataverseStandardCountCap));
    }

    [Fact]
    public void ParseCount_reads_a_string_cap_annotation()
    {
        var count = DualWriteMapParser.ParseCount(
            "{\"@odata.count\":5000,\"@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded\":\"true\",\"value\":[]}");

        Assert.True(count!.CapExceeded);
    }

    [Fact]
    public void ParseCount_leaves_the_cap_unknown_when_the_annotation_is_absent()
    {
        // No annotation (an F&O response, or a Dataverse env that ignored the Prefer header) → tri-state
        // null, so each side can decide: F&O counts aren't capped, a Dataverse count on the limit is.
        var onTheLimit = DualWriteMapParser.ParseCount("{\"@odata.count\":5000,\"value\":[]}");
        Assert.Null(onTheLimit!.CapExceeded);
        Assert.True(onTheLimit.IsCappedAt(DualWriteMapParser.DataverseStandardCountCap));

        var wellUnder = DualWriteMapParser.ParseCount("{\"@odata.count\":4999,\"value\":[]}");
        Assert.Null(wellUnder!.CapExceeded);
        Assert.False(wellUnder.IsCappedAt(DualWriteMapParser.DataverseStandardCountCap));
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
