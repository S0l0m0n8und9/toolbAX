using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IMetadataService"/> seeded from the design prototype (data.js ENTITIES /
/// FIELDS). Only CustomersV3 has cached fields — the rest exercise the "not cached yet" state.
/// </summary>
public sealed class FakeMetadataService : IMetadataService
{
    private static readonly IReadOnlyList<EntitySet> Entities = new[]
    {
        new EntitySet("CustomersV3", "AR", 87, "dataAreaId,CustomerAccount", true, "common"),
        new EntitySet("VendorsV2", "AP", 102, "dataAreaId,VendorAccount", true, "common"),
        new EntitySet("ReleasedProductsV2", "IC", 143, "dataAreaId,ItemNumber", true, "common"),
        new EntitySet("SalesOrderHeadersV2", "SO", 96, "dataAreaId,SalesOrderNumber", true, "transactional"),
        new EntitySet("SalesOrderLinesV2", "SO", 124, "dataAreaId,SalesOrderNumber,LineNum", true, "transactional"),
        new EntitySet("PurchaseOrderHeadersV2", "PO", 78, "dataAreaId,PurchaseOrderNumber", true, "transactional"),
        new EntitySet("LedgerJournalHeaders", "GL", 41, "dataAreaId,JournalNumber", true, "finance"),
        new EntitySet("ChartOfAccounts", "GL", 28, "LedgerChartOfAccounts", false, "finance"),
        new EntitySet("WorkerV2", "HR", 64, "PersonnelNumber", false, "hr"),
        new EntitySet("LegalEntities", "SYS", 22, "LegalEntityId", false, "system"),
    };

    private static readonly Dictionary<string, IReadOnlyList<EntityField>> Fields = new()
    {
        ["CustomersV3"] = new[]
        {
            new EntityField("dataAreaId", "String", false, IsKey: true, Length: 4),
            new EntityField("CustomerAccount", "String", false, IsKey: true, Length: 20),
            new EntityField("OrganizationName", "String", true, Length: 100),
            new EntityField("CustomerGroupId", "String", true, Length: 10),
            new EntityField("CurrencyCode", "String", false, Length: 3),
            new EntityField("PaymentTermsName", "String", true, Length: 10),
            new EntityField("CreditLimit", "Decimal", true, Precision: 32),
            new EntityField("IsOneTime", "Enum", false, EnumType: "NoYes"),
            new EntityField("CreatedDateTime", "DateTime", false),
            new EntityField("ModifiedDateTime", "DateTime", false),
            new EntityField("BlockedForInvoice", "Enum", true, EnumType: "CustVendorBlocked"),
            new EntityField("PrimaryContactEmail", "String", true, Length: 80),
        },
    };

    // Enum members for the enum types referenced by the seeded fields (drives the POST Builder's
    // enum-dropdown cell editors). Case-insensitive to match CoreMetadataService._enums, so the fake
    // resolves enum types the same way the real service does.
    private static readonly Dictionary<string, IReadOnlyList<string>> Enums =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NoYes"] = new[] { "No", "Yes" },
            ["CustVendorBlocked"] = new[] { "No", "Yes", "Invoice", "All" },
        };

    public IReadOnlyList<EntitySet> GetEntities() => Entities;

    public IReadOnlyList<EntityField>? GetFields(string entityName) =>
        Fields.TryGetValue(entityName, out var fields) ? fields : null;

    public IReadOnlyList<string>? GetEnumMembers(string enumType) =>
        Enums.TryGetValue(enumType, out var members) ? members : null;

    // The fake's data is seeded, so loading is a no-op; LoadFieldsAsync reports whether the entity
    // has cached fields (CustomersV3 only) to preserve the "not cached" demo state.
    public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
        Task.FromResult(Fields.ContainsKey(entityName));
}
