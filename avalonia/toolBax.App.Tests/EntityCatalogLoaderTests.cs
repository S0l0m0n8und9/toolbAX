using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// <see cref="EntityCatalogLoader"/> centralises the "load catalogue, diff, fetch fields" logic shared
/// by the Metadata Browser and Query Builder VMs, capturing load failures into <c>LastError</c> rather
/// than letting them vanish into a fire-and-forget command.
/// </summary>
public class EntityCatalogLoaderTests
{
    private sealed class StubMeta : IMetadataService
    {
        public readonly List<EntitySet> Entities = new();
        public readonly Dictionary<string, IReadOnlyList<EntityField>> Fields = new();
        public Exception? EntitiesError;
        public Exception? FieldsError;

        public IReadOnlyList<EntitySet> GetEntities() => Entities;
        public IReadOnlyList<EntityField>? GetFields(string entityName) =>
            Fields.TryGetValue(entityName, out var f) ? f : null;

        public Task LoadEntitiesAsync(CancellationToken ct = default)
        {
            if (EntitiesError is not null) throw EntitiesError;
            return Task.CompletedTask;
        }

        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default)
        {
            if (FieldsError is not null) throw FieldsError;
            Fields[entityName] = new[] { new EntityField("X", "String", false) };
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task LoadEntitiesAsync_returns_the_list_when_it_changed()
    {
        var meta = new StubMeta { Entities = { new EntitySet("A", "M", 1, "k", false, "t") } };
        var loader = new EntityCatalogLoader(meta);

        var loaded = await loader.LoadEntitiesAsync(Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("A", loaded!.Single().Name);
        Assert.Null(loader.LastError);
    }

    [Fact]
    public async Task LoadEntitiesAsync_returns_null_when_unchanged()
    {
        var meta = new StubMeta { Entities = { new EntitySet("A", "M", 1, "k", false, "t") } };
        var loader = new EntityCatalogLoader(meta);

        var loaded = await loader.LoadEntitiesAsync(new[] { "A" }, TestContext.Current.CancellationToken);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadEntitiesAsync_captures_a_failure_into_LastError()
    {
        var meta = new StubMeta { EntitiesError = new InvalidOperationException("metadata unreachable") };
        var loader = new EntityCatalogLoader(meta);

        var loaded = await loader.LoadEntitiesAsync(Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Null(loaded);
        Assert.Contains("unreachable", loader.LastError);
    }

    [Fact]
    public async Task EnsureFieldsAsync_skips_when_already_cached()
    {
        var meta = new StubMeta();
        meta.Fields["A"] = new[] { new EntityField("Id", "String", false) };
        var loader = new EntityCatalogLoader(meta);

        var fetched = await loader.EnsureFieldsAsync("A", TestContext.Current.CancellationToken);

        Assert.False(fetched); // already cached → no fetch
    }

    [Fact]
    public async Task EnsureFieldsAsync_fetches_when_not_cached()
    {
        var meta = new StubMeta();
        var loader = new EntityCatalogLoader(meta);

        var fetched = await loader.EnsureFieldsAsync("A", TestContext.Current.CancellationToken);

        Assert.True(fetched);
        Assert.NotNull(meta.GetFields("A"));
        Assert.Null(loader.LastError);
    }

    [Fact]
    public async Task EnsureFieldsAsync_captures_a_failure_into_LastError()
    {
        var meta = new StubMeta { FieldsError = new InvalidOperationException("token denied") };
        var loader = new EntityCatalogLoader(meta);

        var fetched = await loader.EnsureFieldsAsync("A", TestContext.Current.CancellationToken);

        Assert.False(fetched);
        Assert.Contains("token denied", loader.LastError);
    }

    [Fact]
    public async Task A_successful_load_clears_a_previous_error()
    {
        var meta = new StubMeta { EntitiesError = new InvalidOperationException("boom") };
        var loader = new EntityCatalogLoader(meta);
        await loader.LoadEntitiesAsync(Array.Empty<string>(), TestContext.Current.CancellationToken);
        Assert.NotNull(loader.LastError);

        meta.EntitiesError = null;
        meta.Entities.Add(new EntitySet("A", "M", 1, "k", false, "t"));
        await loader.LoadEntitiesAsync(Array.Empty<string>(), TestContext.Current.CancellationToken);

        Assert.Null(loader.LastError);
    }
}
