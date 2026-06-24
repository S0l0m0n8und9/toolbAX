using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Shared load logic for the Metadata Browser and Query Builder VMs: fetch the entity catalogue from
/// the active environment's live $metadata, diff it against what's shown, and fetch a single entity's
/// fields on demand. Load failures (token acquisition, unreachable endpoint, SQLite I/O) are captured
/// into <see cref="LastError"/> so the VM can surface them instead of the fetch failing silently.
/// </summary>
public sealed class EntityCatalogLoader
{
    private readonly IMetadataService _metadata;
    // Cancels an in-flight field fetch when the user selects a different entity, so a rapid selection
    // change doesn't leave a redundant request running.
    private CancellationTokenSource? _fieldFetch;

    public EntityCatalogLoader(IMetadataService metadata) => _metadata = metadata;

    /// <summary>The last load failure message, or null after a successful (or skipped) load.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Loads the entity list for the active environment. Returns the new list when it differs from
    /// <paramref name="currentNames"/>, or null when unchanged/empty/failed (check <see cref="LastError"/>).
    /// </summary>
    public async Task<IReadOnlyList<EntitySet>?> LoadEntitiesAsync(IReadOnlyList<string> currentNames, CancellationToken ct)
    {
        try
        {
            await _metadata.LoadEntitiesAsync(ct).ConfigureAwait(true);
            LastError = null;
            var loaded = _metadata.GetEntities();
            return loaded.Count > 0 && !currentNames.SequenceEqual(loaded.Select(e => e.Name))
                ? loaded
                : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Fetches an entity's fields if they aren't cached yet, cancelling any earlier in-flight fetch.
    /// Returns true when a fetch completed (so the caller should refresh its field view).
    /// </summary>
    public async Task<bool> EnsureFieldsAsync(string entityName, CancellationToken ct)
    {
        if (_metadata.GetFields(entityName) is not null)
        {
            return false;
        }

        _fieldFetch?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _fieldFetch = cts;
        try
        {
            await _metadata.LoadFieldsAsync(entityName, cts.Token).ConfigureAwait(true);
            LastError = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
        finally
        {
            // Each call disposes the linked CTS it created (a superseded one is disposed when its own
            // awaited fetch unwinds), so no CancellationTokenSource registration is leaked per selection.
            // Clear the field only if it still points at us, so a newer in-flight fetch isn't disturbed.
            if (ReferenceEquals(_fieldFetch, cts))
            {
                _fieldFetch = null;
            }

            cts.Dispose();
        }
    }
}
