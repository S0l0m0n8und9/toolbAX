using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="CoreMetadataService"/> maps FoToolbox.Core's OData index/details (EDM types,
/// keys, length/precision, enums) onto the Avalonia <see cref="EntitySet"/> / <see cref="EntityField"/>
/// models. Uses a stub <see cref="ICatalogService"/> that only seeds GetODataMetadataAsync — the
/// interface's default index/details implementations compute the rest, so no network is touched.
/// </summary>
public class CoreMetadataServiceTests
{
    private static EnvProfile Env() =>
        new("env1", "Env", "contoso.operations.dynamics.com", "tenant", "USMF", "Tier 1", EnvStatus.Connected);

    private static readonly ODataMetadata Seed = new(
        Entities: new[]
        {
            new ODataEntity("CustomersV3", new[]
            {
                new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true, MaxLength: "4"),
                new ODataProperty("CustomerAccount", "Edm.String", Nullable: false, IsKey: true, MaxLength: "20"),
                new ODataProperty("OrganizationName", "Edm.String", Nullable: true, MaxLength: "100"),
                new ODataProperty("CreditLimit", "Edm.Decimal", Nullable: true, Precision: "32"),
                new ODataProperty("IsOneTime", "Microsoft.Dynamics.DataEntities.NoYes", Nullable: false),
                new ODataProperty("CreatedDateTime", "Edm.DateTimeOffset", Nullable: false),
                new ODataProperty("BirthDate", "Edm.Date", Nullable: true),
            }, Array.Empty<ODataNavigationProperty>()),
            new ODataEntity("VendorsV2", new[]
            {
                new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true, MaxLength: "4"),
                new ODataProperty("VendorAccount", "Edm.String", Nullable: false, IsKey: true, MaxLength: "20"),
            }, Array.Empty<ODataNavigationProperty>()),
        },
        Enums: Array.Empty<ODataEnumType>(),
        ETag: null);

    // Implements ICatalogService by seeding only GetODataMetadataAsync; the default interface methods
    // (GetODataEntityIndexAsync / GetODataEntityDetailsAsync) derive index + details from it.
    private sealed class StubCatalog : ICatalogService
    {
        private readonly ODataMetadata _metadata;
        public StubCatalog(ODataMetadata metadata) => _metadata = metadata;

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(_metadata);

        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default)
            => throw new NotImplementedException();
        public string BuildTableBrowserUrl(FoEnvironment env, string tableName) => throw new NotImplementedException();
        public string BuildODataEntityUrl(FoEnvironment env, string entityName) => throw new NotImplementedException();
    }

    private static CoreMetadataService Make(EnvProfile? env = null) =>
        new(new StubCatalog(Seed), () => env ?? Env());

    [Fact]
    public void Entities_are_empty_before_load()
    {
        var svc = Make();
        Assert.Empty(svc.GetEntities());
    }

    [Fact]
    public async Task LoadEntitiesAsync_lists_entities_from_the_index_with_field_counts()
    {
        var svc = Make();

        await svc.LoadEntitiesAsync(TestContext.Current.CancellationToken);

        var entities = svc.GetEntities();
        Assert.Equal(2, entities.Count);
        var customers = entities.Single(e => e.Name == "CustomersV3");
        Assert.Equal(7, customers.FieldCount); // 7 properties on the index item
    }

    [Fact]
    public async Task GetFields_is_null_until_the_entity_is_loaded()
    {
        var svc = Make();
        await svc.LoadEntitiesAsync(TestContext.Current.CancellationToken);

        Assert.Null(svc.GetFields("CustomersV3"));

        await svc.LoadFieldsAsync("CustomersV3", TestContext.Current.CancellationToken);

        Assert.NotNull(svc.GetFields("CustomersV3"));
    }

    [Fact]
    public async Task LoadFieldsAsync_maps_edm_types_keys_length_precision_and_enum()
    {
        var svc = Make();

        var loaded = await svc.LoadFieldsAsync("CustomersV3", TestContext.Current.CancellationToken);

        Assert.True(loaded);
        var fields = svc.GetFields("CustomersV3")!;
        var account = fields.Single(f => f.Name == "CustomerAccount");
        Assert.True(account.IsKey);
        Assert.Equal("String(20)", account.TypeDisplay);

        Assert.Equal("Decimal(32)", fields.Single(f => f.Name == "CreditLimit").TypeDisplay);

        var isOneTime = fields.Single(f => f.Name == "IsOneTime");
        Assert.Equal("Enum", isOneTime.Type);
        Assert.Equal("NoYes", isOneTime.EnumType);
        Assert.Equal("Enum<NoYes>", isOneTime.TypeDisplay);

        Assert.Equal("DateTime", fields.Single(f => f.Name == "CreatedDateTime").TypeDisplay);
    }

    [Fact]
    public async Task LoadFieldsAsync_keeps_Edm_Date_distinct_from_Edm_DateTimeOffset()
    {
        var svc = Make();

        var loaded = await svc.LoadFieldsAsync("CustomersV3", TestContext.Current.CancellationToken);

        Assert.True(loaded);
        var fields = svc.GetFields("CustomersV3")!;
        // Collapsing Date into DateTime made the payload builder's date-only branch unreachable: every date
        // was widened to Edm.DateTimeOffset and sent as a full timestamp.
        Assert.Equal("Date", fields.Single(f => f.Name == "BirthDate").Type);
        Assert.Equal("DateTime", fields.Single(f => f.Name == "CreatedDateTime").Type);
    }

    [Fact]
    public async Task LoadFieldsAsync_returns_false_for_an_unknown_entity()
    {
        var svc = Make();

        var loaded = await svc.LoadFieldsAsync("NoSuchEntity", TestContext.Current.CancellationToken);

        Assert.False(loaded);
        Assert.Null(svc.GetFields("NoSuchEntity"));
    }

    [Fact]
    public async Task Load_is_a_no_op_without_an_active_environment()
    {
        var svc = new CoreMetadataService(new StubCatalog(Seed), () => null);

        await svc.LoadEntitiesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(svc.GetEntities());
    }
}
