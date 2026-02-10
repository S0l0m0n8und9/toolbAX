using FoToolbox.Core.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace QueryBuilderPlugin;

internal sealed class SavedQueryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new FilterDtoJsonConverter() }
    };

    private readonly ProfileStore _store;
    private Task? _ensureCreatedTask;

    public SavedQueryStore(string dbPath)
    {
        _store = new ProfileStore(dbPath);
    }

    private Task EnsureCreatedAsync()
    {
        // Don't allow cancellation to prevent vault/db initialization. Callers can cancel their own work later.
        var existing = Volatile.Read(ref _ensureCreatedTask);
        if (existing is not null) return existing;

        var created = _store.EnsureCreatedAsync();
        var prior = Interlocked.CompareExchange(ref _ensureCreatedTask, created, null);
        return prior ?? created;
    }

    public async Task<IEnumerable<SavedQueryItem>> LoadForEnvAsync(string envId)
    {
        await EnsureCreatedAsync().ConfigureAwait(false);
        var records = await _store.GetSavedQueriesAsync(envId);
        return records.Select(r => Deserialize(r));
    }

    public async Task SaveAsync(SavedQueryItem item)
    {
        await EnsureCreatedAsync().ConfigureAwait(false);
        var record = new SavedQueryRecord(
            string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
            item.EnvId,
            item.Name,
            JsonSerializer.Serialize(item, SerializerOptions),
            item.CrossCompany,
            item.CreatedUtc ?? DateTime.UtcNow.ToString("o"),
            DateTime.UtcNow.ToString("o"));

        await _store.SaveQueryAsync(record);
    }

    public async Task DeleteAsync(SavedQueryItem item)
    {
        await EnsureCreatedAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(item.Id)) return;
        await _store.DeleteQueryAsync(item.Id);
    }

    private static SavedQueryItem Deserialize(SavedQueryRecord record)
    {
        try
        {
            var model = JsonSerializer.Deserialize<SavedQueryItem>(record.SpecJson, SerializerOptions) ?? new SavedQueryItem();
            model.Id = record.Id;
            model.EnvId = record.EnvId;
            model.Name = record.Name;
            model.CrossCompany = record.CrossCompany;
            model.CreatedUtc = record.CreatedUtc;
            model.UpdatedUtc = record.UpdatedUtc;
            return model;
        }
        catch
        {
            return new SavedQueryItem
            {
                Id = record.Id,
                EnvId = record.EnvId,
                Name = record.Name,
                CrossCompany = record.CrossCompany,
                CreatedUtc = record.CreatedUtc,
                UpdatedUtc = record.UpdatedUtc
            };
        }
    }
}
