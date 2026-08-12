using System;
using System.Collections.Generic;
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
/// <see cref="CoreMetadataService"/> is an app-lifetime singleton, so its in-memory caches must be
/// scoped to the environment they were fetched from: after an active-environment switch the getters
/// have to miss (callers such as <see cref="ViewModels.EntityCatalogLoader"/> treat a hit as
/// "already loaded" and never refetch, which would pin the previous environment's metadata).
/// </summary>
public class CoreMetadataServiceEnvScopeTests
{
    private const string EntityName = "CustomersV3";

    private static EnvProfile Env(string id) =>
        new(id, id, $"{id}.operations.dynamics.com", "tenant", "USMF", "Tier 1", EnvStatus.Connected);

    // Both environments expose the same entity name but different property and enum names, so metadata
    // leaking across a switch shows up as the wrong names rather than merely as a stale cache hit.
    private static ODataMetadata Seed(string envId) => new(
        Entities: new[]
        {
            new ODataEntity(EntityName, new[]
            {
                new ODataProperty($"{envId}Field", "Edm.String", Nullable: false, IsKey: true, MaxLength: "20"),
            }, new[] { new ODataNavigationProperty($"{envId}Nav", "Default.Other") }),
        },
        Enums: new[] { new ODataEnumType($"Default.{envId}Enum", new[] { "No", "Yes" }) },
        ETag: null);

    // Serves whichever environment it is asked about; the interface's default index/details methods
    // derive the rest from GetODataMetadataAsync, so no network is touched. An optional gate holds the
    // fetch open so a load can be caught mid-flight.
    private sealed class PerEnvCatalog : ICatalogService
    {
        private readonly Func<Task>? _gate;

        public PerEnvCatalog(Func<Task>? gate = null) => _gate = gate;

        public async Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
        {
            if (_gate is not null)
            {
                await _gate().ConfigureAwait(false);
            }

            return Seed(env.Id);
        }

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

    private static IReadOnlyList<string> FieldNames(CoreMetadataService svc)
    {
        var fields = svc.GetFields(EntityName);
        Assert.NotNull(fields);
        var names = new List<string>();
        foreach (var f in fields!)
        {
            names.Add(f.Name);
        }

        return names;
    }

    [Fact]
    public async Task Switching_the_active_environment_empties_the_caches()
    {
        var active = Env("envA");
        var svc = new CoreMetadataService(new PerEnvCatalog(), () => active);
        var ct = TestContext.Current.CancellationToken;

        await svc.LoadEntitiesAsync(ct);
        await svc.LoadFieldsAsync(EntityName, ct);
        Assert.NotEmpty(svc.GetEntities());
        Assert.Contains("envAField", FieldNames(svc));
        Assert.NotNull(svc.GetNavigations(EntityName));
        Assert.NotNull(svc.GetEnumMembers("envAEnum"));

        active = Env("envB");

        Assert.Null(svc.GetFields(EntityName));
        Assert.Empty(svc.GetEntities());
        Assert.Null(svc.GetNavigations(EntityName));
        Assert.Null(svc.GetEnumMembers("envAEnum"));
    }

    [Fact]
    public async Task Reloading_after_a_switch_serves_the_new_environments_metadata()
    {
        var active = Env("envA");
        var svc = new CoreMetadataService(new PerEnvCatalog(), () => active);
        var ct = TestContext.Current.CancellationToken;

        await svc.LoadEntitiesAsync(ct);
        await svc.LoadFieldsAsync(EntityName, ct);

        active = Env("envB");
        await svc.LoadEntitiesAsync(ct);
        await svc.LoadFieldsAsync(EntityName, ct);

        Assert.Contains("envBField", FieldNames(svc));
        Assert.DoesNotContain("envAField", FieldNames(svc));
        Assert.NotNull(svc.GetEnumMembers("envBEnum"));
    }

    // A gate that reports when the fetch has been entered, then blocks until the test releases it. The
    // capture of the cache generation happens before the first await, so awaiting Entered means "env A's
    // load is past that point and pending".
    private sealed class FetchGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public Func<Task> Hold => () =>
        {
            _entered.TrySetResult();
            return _release.Task;
        };

        public void Release() => _release.TrySetResult();
    }

    // A load resolves its environment at entry and then awaits. If the active environment switches while
    // that fetch is in flight, a getter re-stamps the cache to the new environment — so the late result
    // must be dropped, not committed into a cache now labelled as the other environment's.
    [Fact]
    public async Task An_entity_load_completing_after_a_switch_is_discarded()
    {
        var active = Env("envA");
        var gate = new FetchGate();
        var svc = new CoreMetadataService(new PerEnvCatalog(gate.Hold), () => active);
        var ct = TestContext.Current.CancellationToken;

        var load = svc.LoadEntitiesAsync(ct);
        await gate.Entered;                       // envA's fetch is pending

        active = Env("envB");
        Assert.Empty(svc.GetEntities());           // the getter re-stamps the cache to envB

        gate.Release();
        await load;

        // envA's result arrived too late to belong to anything: it must not resurface as envB's.
        Assert.Empty(svc.GetEntities());
        Assert.Null(svc.GetEnumMembers("envAEnum"));
    }

    [Fact]
    public async Task A_field_load_completing_after_a_switch_is_discarded_and_reports_not_loaded()
    {
        var active = Env("envA");
        var gate = new FetchGate();
        var svc = new CoreMetadataService(new PerEnvCatalog(gate.Hold), () => active);
        var ct = TestContext.Current.CancellationToken;

        var load = svc.LoadFieldsAsync(EntityName, ct);
        await gate.Entered;

        active = Env("envB");
        Assert.Null(svc.GetFields(EntityName));

        gate.Release();

        // False, not true: the entity is simply not loaded for the now-active environment, so callers
        // reload rather than trusting a hit that holds envA's properties.
        Assert.False(await load);
        Assert.Null(svc.GetFields(EntityName));
        Assert.Null(svc.GetNavigations(EntityName));
    }

    [Fact]
    public async Task Switching_back_does_not_resurrect_the_first_environments_cache()
    {
        var active = Env("envA");
        var svc = new CoreMetadataService(new PerEnvCatalog(), () => active);
        var ct = TestContext.Current.CancellationToken;

        await svc.LoadEntitiesAsync(ct);
        await svc.LoadFieldsAsync(EntityName, ct);

        active = Env("envB");
        await svc.LoadEntitiesAsync(ct);
        await svc.LoadFieldsAsync(EntityName, ct);

        active = Env("envA");

        Assert.Null(svc.GetFields(EntityName));
        Assert.Empty(svc.GetEntities());
    }
}
