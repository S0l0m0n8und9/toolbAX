using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ODataPostBuilderPlugin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

/// <summary>
/// Regression tests for the OData API Builder "Load Entities" flow (#37): the first load must not
/// report a false failure, and repeated presses must not start duplicate concurrent loads.
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
        RunSta(async () =>
        {
            var uiThreadId = Environment.CurrentManagedThreadId;
            var ctx = new FakeContext(entityCount: 3);
            var vm = new ODataPostBuilderViewModel(ctx);

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
            // thread-pool thread — otherwise WPF throws and the load reports a false failure.
            Assert.Equal(uiThreadId, finallyThreadId);
            Assert.Equal("Loaded 3 entities.", vm.EntityLoadStatus);
        });
    }

    [Fact]
    public void LoadEntities_ignores_a_second_press_while_a_load_is_in_progress()
    {
        RunSta(async () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ctx = new FakeContext(entityCount: 2, gate: gate);
            var vm = new ODataPostBuilderViewModel(ctx);

            var first = vm.LoadEntitiesCommand.ExecuteAsync(CancellationToken.None);
            var second = vm.LoadEntitiesCommand.ExecuteAsync(CancellationToken.None);
            gate.SetResult();
            await Task.WhenAll(first, second);

            // The second press arrived while the first load was still in flight, so it must be a no-op
            // rather than kicking off a duplicate concurrent fetch.
            Assert.Equal(1, ctx.Catalog.IndexCallCount);
        });
    }

    private sealed class FakeContext : IPluginContext
    {
        public FakeContext(int entityCount, TaskCompletionSource? gate = null)
        {
            Catalog = new FakeCatalog(entityCount, gate);
        }

        public FoEnvironment CurrentEnv { get; set; } =
            new("dev", "Dev", "https://contoso.operations.dynamics.com", "00000000-0000-0000-0000-000000000000", "USMF");
        public IODataClient OData { get; } = new StubODataClient();
        public FakeCatalog Catalog { get; }
        ICatalogService IPluginContext.Catalog => Catalog;
        public ILogger Logger { get; } = NullLogger.Instance;
    }

    private sealed class FakeCatalog : ICatalogService
    {
        private readonly int _entityCount;
        private readonly TaskCompletionSource? _gate;
        private int _indexCallCount;

        public FakeCatalog(int entityCount, TaskCompletionSource? gate)
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
                // Force a genuine async boundary that completes on a thread-pool thread, mimicking a
                // cache-miss network fetch (so the caller's continuation is exposed to thread hops).
                await Task.Run(() => { }, ct).ConfigureAwait(false);
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

    private sealed class StubODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, CancellationToken cancellationToken = default)
            => ODataClientExtensions.EmptyPages(cancellationToken);
    }

    // Runs an async test body on a dedicated STA thread whose awaited continuations are pumped back
    // onto that same thread (so the test models a single UI thread). Mirrors the helper in
    // PluginManagerTests; see issue #38.
    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previousContext = SynchronizationContext.Current;
            var syncContext = new SingleThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                var task = action();
                task.ContinueWith(_ => syncContext.Complete(), TaskScheduler.Default);
                syncContext.RunOnCurrentThread();
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw failure;
        }
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void RunOnCurrentThread()
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work.Callback(work.State);
            }
        }

        public void Complete() => _queue.CompleteAdding();
    }
}
