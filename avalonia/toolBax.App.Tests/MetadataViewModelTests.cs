using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class MetadataViewModelTests
{
    private static MetadataViewModel MakeVm() => new(new FakeMetadataService());

    // Mimics the real service: nothing is available until LoadEntitiesAsync runs, then one entity with
    // fields appears — so InitializeAsync (not the ctor) is what populates the browser.
    private sealed class DeferredMetadata : IMetadataService
    {
        private bool _loaded;
        private static readonly EntitySet[] Late = { new("LateEntity", "M", 1, "k", false, "odata") };
        private static readonly EntityField[] LateFields = { new("Id", "String", false, IsKey: true, Length: 10) };

        public IReadOnlyList<EntitySet> GetEntities() => _loaded ? Late : Array.Empty<EntitySet>();
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            _loaded && entityName == "LateEntity" ? LateFields : null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) { _loaded = true; return Task.CompletedTask; }
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        { _loaded = true; return Task.FromResult(entityName == "LateEntity"); }
    }

    [Fact]
    public async Task Initialize_loads_entities_and_fields_unavailable_at_construction()
    {
        var vm = new MetadataViewModel(new DeferredMetadata());
        Assert.Empty(vm.Entities); // nothing seeded at ctor — the real service starts empty

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Contains(vm.Entities, e => e.Name == "LateEntity");
        Assert.True(vm.IsCached);           // the first entity's fields were auto-fetched
        Assert.NotEmpty(vm.Fields);
    }

    // Mimics a live failure (token denied / OData unreachable) so the fetch can't silently no-op.
    private sealed class ThrowingMetadata : IMetadataService
    {
        public IReadOnlyList<EntitySet> GetEntities() => Array.Empty<EntitySet>();
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("metadata endpoint unreachable"));
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) =>
            Task.FromException<bool>(new InvalidOperationException("metadata endpoint unreachable"));
    }

    [Fact]
    public async Task Initialize_surfaces_a_load_failure_as_LoadError()
    {
        var vm = new MetadataViewModel(new ThrowingMetadata());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Contains("unreachable", vm.LoadError);
        Assert.Empty(vm.Entities);
    }

    [Fact]
    public void Cached_entity_populates_fields()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.IsCached);
        Assert.NotEmpty(vm.Fields);
        Assert.Contains(vm.Fields, f => f.Name == "CustomerAccount" && f.IsKey);
    }

    [Fact]
    public void Uncached_entity_shows_no_fields_and_a_fetch_hint()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "VendorsV2");

        Assert.False(vm.IsCached);
        Assert.Empty(vm.Fields);
        Assert.Contains("Query Builder", vm.NotCachedMessage);
    }

    [Fact]
    public void Filter_matches_name_or_module()
    {
        var vm = MakeVm();

        vm.Search = "CustomersV3";
        Assert.Single(vm.Filtered);

        vm.Search = "GL";
        Assert.NotEmpty(vm.Filtered);
        Assert.All(vm.Filtered, e => Assert.Equal("GL", e.Module));
    }

    [Fact]
    public void Type_display_formats_string_enum_and_decimal()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.Equal("String(20)", vm.Fields.Single(f => f.Name == "CustomerAccount").TypeDisplay);
        Assert.Equal("Enum<NoYes>", vm.Fields.Single(f => f.Name == "IsOneTime").TypeDisplay);
        Assert.Equal("Decimal(32)", vm.Fields.Single(f => f.Name == "CreditLimit").TypeDisplay);
    }
}
