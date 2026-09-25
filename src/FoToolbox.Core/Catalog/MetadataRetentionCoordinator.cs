using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Catalog;

/// <summary>
/// Process-local admission and pruning for a file-backed catalog. The gate is never held during HTTP,
/// authentication or a per-partition lock wait. It is not a cross-process lease or database-size limit.
/// </summary>
internal sealed class MetadataRetentionCoordinator
{
    // The app normally has one catalog path. Keep coordinators alive for the process: disposing or
    // removing one while another store still owns a lease would split the exclusion domain.
    private static readonly ConcurrentDictionary<string, MetadataRetentionCoordinator> Coordinators =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, int> _leases = new(StringComparer.Ordinal);
    private long _pruneGeneration;

    internal static MetadataRetentionCoordinator ForDatabase(string path) =>
        Coordinators.GetOrAdd(Path.GetFullPath(path), _ => new MetadataRetentionCoordinator());

    internal long PruneGeneration => Volatile.Read(ref _pruneGeneration);

    internal async ValueTask<IAsyncDisposable> AcquireAsync(CatalogStore store, string profileId, string key, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _leases.TryGetValue(key, out var count);
            _leases[key] = count + 1;
        }
        finally
        {
            _gate.Release();
        }
        return new Lease(this, store, profileId, key);
    }

    private async ValueTask ReleaseAsync(CatalogStore store, string profileId, string key)
    {
        // Unconditional bookkeeping: caller cancellation must never strand a protected partition.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_leases[key] == 1) _leases.Remove(key);
            else _leases[key]--;

            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                if (await store.PruneMetadataPartitionsAsync(profileId, _leases.Keys, cleanup.Token).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref _pruneGeneration);
                }
            }
            catch (Exception ex)
            {
                // Housekeeping may temporarily leave excess rows, but never replace successful data or
                // the original fetch exception. Even a hostile Trace listener must not change that.
                try { Trace.TraceWarning("Metadata cache cleanup deferred ({0}).", ex.GetType().Name); }
                catch { }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class Lease(MetadataRetentionCoordinator owner, CatalogStore store, string profileId, string key) : IAsyncDisposable
    {
        private MetadataRetentionCoordinator? _owner = owner;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _owner, null) is { } retained
            ? retained.ReleaseAsync(store, profileId, key)
            : ValueTask.CompletedTask;
    }
}
