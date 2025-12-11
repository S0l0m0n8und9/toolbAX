using FoToolbox.Core.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace QueryBuilderPlugin;

internal sealed class SavedQueryStore
{
    private readonly ProfileStore _store;

    public SavedQueryStore(string dbPath)
    {
        _store = new ProfileStore(dbPath);
        _store.EnsureCreatedAsync().GetAwaiter().GetResult();
    }

    public async Task<IEnumerable<SavedQueryItem>> LoadForEnvAsync(string envId)
    {
        var records = await _store.GetSavedQueriesAsync(envId);
        return records.Select(r => Deserialize(r));
    }

    public async Task SaveAsync(SavedQueryItem item)
    {
        var record = new SavedQueryRecord(
            string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
            item.EnvId,
            item.Name,
            JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = false }),
            item.CrossCompany,
            item.CreatedUtc ?? DateTime.UtcNow.ToString("o"),
            DateTime.UtcNow.ToString("o"));

        await _store.SaveQueryAsync(record);
    }

    public async Task DeleteAsync(SavedQueryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id)) return;
        await _store.DeleteQueryAsync(item.Id);
    }

    private static SavedQueryItem Deserialize(SavedQueryRecord record)
    {
        var model = JsonSerializer.Deserialize<SavedQueryItem>(record.SpecJson) ?? new SavedQueryItem();
        model.Id = record.Id;
        model.CreatedUtc = record.CreatedUtc;
        model.UpdatedUtc = record.UpdatedUtc;
        return model;
    }
}
