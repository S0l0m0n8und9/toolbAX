using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode / test <see cref="IDualWriteMapReader"/>: returns a small, deterministic set of
/// dual-write maps. The seed is a real <c>msdyn_dualwriteentitymaps</c>-shaped JSON document run
/// through <see cref="DualWriteMapParser"/>, so the fake exercises the production parse path and the
/// records match the live shape exactly (no separate hand-built models to drift).
/// </summary>
public sealed class FakeDualWriteMapReader : IDualWriteMapReader
{
    private static readonly DwMapLoadResult Seed =
        DwMapLoadResult.Ok(DualWriteMapParser.ParsePage(SeedJson).Records);

    public Task<DwMapLoadResult> GetMapsAsync(CancellationToken ct = default) => Task.FromResult(Seed);

    private const string SeedJson = """
    {
      "value": [
        {
          "msdyn_dualwriteentitymapid": "10000000-0000-0000-0000-000000000001",
          "solutionid": "aaaaaaaa-0000-0000-0000-000000000001",
          "msdyn_name": "customersv3_account",
          "msdyn_displayname": "Customers V3 to Accounts",
          "msdyn_version": "1.0.0.12",
          "createdon": "2024-02-01T09:00:00Z",
          "modifiedon": "2024-06-01T11:30:00Z",
          "statecode@OData.Community.Display.V1.FormattedValue": "Active",
          "statuscode@OData.Community.Display.V1.FormattedValue": "Published",
          "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Dual Write Service",
          "msdyn_mapping": "{\"id\":\"map-cust\",\"description\":\"customer master\",\"legs\":[{\"id\":\"leg-1\",\"sourceSchema\":\"CustCustomerV3Entity\",\"sourceSchemaDistinctName\":\"CustCustomerV3Entity (Distinct)\",\"destinationSchema\":\"accounts\",\"sourceEnvironmentType\":\"AX\",\"destinationEnvironmentType\":\"CRM\",\"sourceFilter\":\"CustomerGroupId == 'DOM'\",\"reversedSourceFilter\":\"accounttype eq 'customer'\",\"fieldMappings\":[{\"id\":\"fm-1\",\"sourceField\":\"CustomerAccount\",\"destinationField\":\"accountnumber\",\"syncDirection\":\"Bidirectional\",\"destinationLookupFieldRelatedEntity\":null,\"isSystemGenerated\":false,\"valueTransforms\":[]},{\"id\":\"fm-2\",\"sourceField\":\"CurrencyCode\",\"destinationField\":\"transactioncurrencyid\",\"syncDirection\":\"Forward\",\"destinationLookupFieldRelatedEntity\":\"transactioncurrency\",\"isSystemGenerated\":true,\"valueTransforms\":[]},{\"id\":\"fm-3\",\"sourceField\":\"CustomerGroupId\",\"destinationField\":\"cdm_customergroup\",\"syncDirection\":\"Bidirectional\",\"destinationLookupFieldRelatedEntity\":null,\"isSystemGenerated\":false,\"valueTransforms\":[{\"sourceField\":\"CustomerGroupId\",\"destinationField\":\"cdm_customergroup\",\"transformType\":\"ValueMap\",\"defaultValue\":\"10\",\"createValuesOnDestination\":false,\"valueMap\":{\"10\":\"Wholesale\",\"20\":\"Retail\",\"30\":\"Distribution\"}}]}]}]}",
          "msdyn_properties": "{\"IntegrationKey\":\"CustomerAccount\",\"IsActive\":true,\"SyncIntervalMinutes\":15}"
        },
        {
          "msdyn_dualwriteentitymapid": "10000000-0000-0000-0000-000000000002",
          "solutionid": "aaaaaaaa-0000-0000-0000-000000000001",
          "msdyn_name": "vendorsv2_account",
          "msdyn_displayname": "Vendors V2 to Accounts",
          "msdyn_version": "1.0.0.8",
          "createdon": "2024-02-03T09:00:00Z",
          "modifiedon": "2024-05-20T08:15:00Z",
          "statecode@OData.Community.Display.V1.FormattedValue": "Active",
          "statuscode@OData.Community.Display.V1.FormattedValue": "Published",
          "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Dual Write Service",
          "msdyn_mapping": "{\"id\":\"map-vend\",\"legs\":[{\"id\":\"leg-1\",\"sourceSchema\":\"VendVendorV2Entity\",\"sourceSchemaDistinctName\":\"VendVendorV2Entity (Distinct)\",\"destinationSchema\":\"accounts\",\"sourceEnvironmentType\":\"AX\",\"destinationEnvironmentType\":\"CRM\",\"sourceFilter\":\"\",\"reversedSourceFilter\":\"\",\"fieldMappings\":[{\"id\":\"fm-1\",\"sourceField\":\"VendorAccountNumber\",\"destinationField\":\"accountnumber\",\"syncDirection\":\"Forward\",\"isSystemGenerated\":false,\"valueTransforms\":[]}]}]}",
          "msdyn_properties": "{\"IntegrationKey\":\"VendorAccountNumber\",\"IsActive\":true}"
        },
        {
          "msdyn_dualwriteentitymapid": "10000000-0000-0000-0000-000000000003",
          "solutionid": "aaaaaaaa-0000-0000-0000-000000000002",
          "msdyn_name": "salesorderheaders_salesorder",
          "msdyn_displayname": "Sales Order Headers V2 to Sales Orders",
          "msdyn_version": "1.0.0.15",
          "createdon": "2024-03-10T09:00:00Z",
          "modifiedon": "2024-06-04T16:45:00Z",
          "statecode@OData.Community.Display.V1.FormattedValue": "Inactive",
          "statuscode@OData.Community.Display.V1.FormattedValue": "Draft",
          "_ownerid_value@OData.Community.Display.V1.FormattedValue": "System Administrator",
          "msdyn_mapping": "{\"id\":\"map-so\",\"legs\":[{\"id\":\"leg-1\",\"sourceSchema\":\"SalesOrderHeaderV2Entity\",\"sourceSchemaDistinctName\":\"SalesOrderHeaderV2Entity (Distinct)\",\"destinationSchema\":\"salesorders\",\"sourceEnvironmentType\":\"AX\",\"destinationEnvironmentType\":\"CRM\",\"sourceFilter\":\"\",\"reversedSourceFilter\":\"\",\"fieldMappings\":[{\"id\":\"fm-1\",\"sourceField\":\"SalesOrderNumber\",\"destinationField\":\"ordernumber\",\"syncDirection\":\"Backward\",\"isSystemGenerated\":false,\"valueTransforms\":[]}]}]}",
          "msdyn_properties": "{\"IntegrationKey\":\"SalesOrderNumber\",\"IsActive\":false}"
        }
      ]
    }
    """;
}
