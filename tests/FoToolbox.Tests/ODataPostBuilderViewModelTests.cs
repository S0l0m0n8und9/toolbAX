using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.TestHelpers;
using ODataPostBuilderPlugin;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

/// <summary>
/// View-model flow tests for the OData API Builder plugin (#37, #34). They run the view model on a
/// pumped STA "UI thread" (<see cref="UiThreadTestHost"/>) with fake services (<see cref="FakePluginContext"/>),
/// so plugin flows are covered without a visible desktop session.
/// </summary>
public sealed class ODataPostBuilderViewModelTests : IDisposable
{
    private readonly string? _previousAppDataDir;
    private readonly string _tempAppDataDir;

    public ODataPostBuilderViewModelTests()
    {
        // Isolate the saved-request store the view model opens in its constructor (parallelization is
        // disabled assembly-wide, so mutating this process env var is safe for the test's duration).
        _previousAppDataDir = Environment.GetEnvironmentVariable("FOTOOLBOX_APPDATA_DIR");
        _tempAppDataDir = Directory.CreateTempSubdirectory("odata-vm-tests").FullName;
        Environment.SetEnvironmentVariable("FOTOOLBOX_APPDATA_DIR", _tempAppDataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FOTOOLBOX_APPDATA_DIR", _previousAppDataDir);
        try { Directory.Delete(_tempAppDataDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void LoadEntities_first_load_succeeds_and_continues_on_the_calling_thread()
    {
        UiThreadTestHost.Run(async () =>
        {
            var uiThreadId = Environment.CurrentManagedThreadId;
            var vm = new ODataPostBuilderViewModel(new FakePluginContext(new DeferredCatalog(entityCount: 3)));

            // The continuation runs the finally that clears IsBusy; capture the thread it runs on.
            var finallyThreadId = -1;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ODataPostBuilderViewModel.IsBusy) && !vm.IsBusy)
                {
                    finallyThreadId = Environment.CurrentManagedThreadId;
                }
            };

            await vm.LoadEntitiesCommand.ExecuteAsync(CancellationToken.None);

            // The fetch genuinely suspends (cache miss / real async). The continuation mutates the
            // UI-bound entity collection, so it must resume on the calling (UI) thread — not a
            // thread-pool thread — otherwise WPF throws and the load reports a false failure (#37).
            Assert.Equal(uiThreadId, finallyThreadId);
            Assert.Equal("Loaded 3 entities.", vm.EntityLoadStatus);
        });
    }

    [Fact]
    public void LoadEntities_ignores_a_second_press_while_a_load_is_in_progress()
    {
        UiThreadTestHost.Run(async () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var catalog = new DeferredCatalog(entityCount: 2, gate: gate);
            var vm = new ODataPostBuilderViewModel(new FakePluginContext(catalog));

            var first = vm.LoadEntitiesCommand.ExecuteAsync(CancellationToken.None);
            var second = vm.LoadEntitiesCommand.ExecuteAsync(CancellationToken.None);
            gate.SetResult();
            await Task.WhenAll(first, second);

            // The second press arrived while the first load was still in flight, so it must be a no-op
            // rather than kicking off a duplicate concurrent fetch.
            Assert.Equal(1, catalog.IndexCallCount);
        });
    }

    [Fact]
    public void LoadEntities_populates_entities_from_the_catalog()
    {
        UiThreadTestHost.Run(async () =>
        {
            // FakePluginContext defaults to the seeded FakeCatalogService (a single "Customers" entity).
            var vm = new ODataPostBuilderViewModel(new FakePluginContext());

            await vm.LoadEntitiesCommand.ExecuteAsync(CancellationToken.None);

            Assert.Equal("Loaded 1 entities.", vm.EntityLoadStatus);
            Assert.Contains(vm.Entities, e => e.Name == "Customers");
        });
    }

    /// <summary>
    /// An <see cref="ICatalogService"/> whose entity-index load completes on a thread-pool thread
    /// (mimicking a cache-miss network fetch), optionally blocked on a gate so a test can hold a load
    /// "in progress". Only the members exercised by these flows are implemented.
    /// </summary>
    private sealed class DeferredCatalog : ICatalogService
    {
        private readonly int _entityCount;
        private readonly TaskCompletionSource? _gate;
        private int _indexCallCount;

        public DeferredCatalog(int entityCount, TaskCompletionSource? gate = null)
        {
            _entityCount = entityCount;
            _gate = gate;
        }

        public int IndexCallCount => _indexCallCount;

        public async Task<ODataEntityIndex> GetODataEntityIndexAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _indexCallCount);
            if (_gate is not null)
            {
                await _gate.Task.ConfigureAwait(false);
            }
            else
            {
                await Task.Run(() => { }, ct).ConfigureAwait(false); // genuine async boundary on a pool thread
            }

            var items = Enumerable.Range(0, _entityCount)
                .Select(i => new ODataEntityIndexItem($"Entity{i}", 1, 0))
                .ToList();
            return new ODataEntityIndex(items, Array.Empty<ODataEnumType>(), null);
        }

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default)
            => throw new NotSupportedException();
        public string BuildTableBrowserUrl(FoEnvironment env, string tableName) => string.Empty;
        public string BuildODataEntityUrl(FoEnvironment env, string entityName) => $"{env.BaseUrl}/data/{entityName}";
    }
}
