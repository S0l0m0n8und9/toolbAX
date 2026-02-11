using FoToolbox.Core.Profiles;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ODataPostBuilderPlugin;

internal sealed class SavedApiRequestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly ProfileStore _store;
    private readonly ILogger? _logger;
    private Task? _ensureCreatedTask;

    public SavedApiRequestStore(string dbPath, ILogger? logger = null)
    {
        _store = new ProfileStore(dbPath);
        _logger = logger;
    }

    private Task EnsureCreatedAsync()
    {
        var existing = Volatile.Read(ref _ensureCreatedTask);
        if (existing is not null) return existing;

        var created = _store.EnsureCreatedAsync();
        var prior = Interlocked.CompareExchange(ref _ensureCreatedTask, created, null);
        return prior ?? created;
    }

    public async Task<IReadOnlyList<SavedApiRequestItem>> LoadForEnvAsync(string envId)
    {
        await EnsureCreatedAsync().ConfigureAwait(false);
        var records = await _store.GetSavedApiRequestsAsync(envId).ConfigureAwait(false);
        return records.Select(Deserialize).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task SaveAsync(SavedApiRequestItem item)
    {
        await EnsureCreatedAsync().ConfigureAwait(false);

        var now = DateTime.UtcNow.ToString("o");
        var id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;

        var doc = new OpenCollectionRequestDoc
        {
            Info = new OpenCollectionInfo { Name = item.Name, Seq = 1, Type = "http" },
            Http = new OpenCollectionHttp
            {
                Method = item.Method,
                Url = item.Url,
                Body = string.IsNullOrWhiteSpace(item.BodyJson) ? null : new OpenCollectionBody { Type = "json", Data = item.BodyJson },
                Headers = item.Headers is null || item.Headers.Count == 0
                    ? null
                    : item.Headers.Select(kvp => new OpenCollectionHeader { Name = kvp.Key, Value = kvp.Value }).ToList()
            }
        };

        var json = JsonSerializer.Serialize(doc, SerializerOptions);
        var record = new SavedApiRequestRecord(
            id,
            item.EnvId,
            item.Name,
            item.Method,
            item.Url,
            json,
            item.CreatedUtc ?? now,
            now);

        await _store.SaveApiRequestAsync(record).ConfigureAwait(false);
        item.Id = id;
        item.CreatedUtc ??= record.CreatedUtc;
        item.UpdatedUtc = record.UpdatedUtc;
    }

    public async Task DeleteAsync(SavedApiRequestItem item)
    {
        await EnsureCreatedAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(item.Id)) return;
        await _store.DeleteApiRequestAsync(item.Id).ConfigureAwait(false);
    }

    public string ExportAllAsOpenCollection(string collectionName, IReadOnlyList<SavedApiRequestItem> items)
    {
        var doc = new OpenCollectionCollectionDoc
        {
            OpenCollection = "1.0.0",
            Info = new OpenCollectionCollectionInfo { Name = string.IsNullOrWhiteSpace(collectionName) ? "FoToolbox API" : collectionName },
            Items = items.Select(i => new OpenCollectionRequestDoc
            {
                Info = new OpenCollectionInfo { Name = i.Name, Seq = 1, Type = "http" },
                Http = new OpenCollectionHttp
                {
                    Method = i.Method,
                    Url = i.Url,
                    Body = string.IsNullOrWhiteSpace(i.BodyJson) ? null : new OpenCollectionBody { Type = "json", Data = i.BodyJson },
                    Headers = i.Headers is null || i.Headers.Count == 0
                        ? null
                        : i.Headers.Select(kvp => new OpenCollectionHeader { Name = kvp.Key, Value = kvp.Value }).ToList()
                }
            }).ToList()
        };
        return JsonSerializer.Serialize(doc, SerializerOptions);
    }

    public IReadOnlyList<SavedApiRequestItem> ImportOpenCollection(string json, string envId)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<SavedApiRequestItem>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                var list = new List<SavedApiRequestItem>();
                CollectHttpRequests(itemsEl, envId, list);
                return list;
            }

            var single = TryDeserializeRequest(json, envId);
            return single is null ? Array.Empty<SavedApiRequestItem>() : new[] { single };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import OpenCollection JSON.");
            return Array.Empty<SavedApiRequestItem>();
        }
    }

    private void CollectHttpRequests(JsonElement itemsArray, string envId, List<SavedApiRequestItem> list)
    {
        foreach (var el in itemsArray.EnumerateArray())
        {
            // HTTP request item
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("http", out _))
            {
                var one = TryDeserializeRequest(el.GetRawText(), envId);
                if (one is not null) list.Add(one);
                continue;
            }

            // Folder items can contain nested items. (OpenCollection "Folder" type)
            if (el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("info", out var infoEl) &&
                infoEl.ValueKind == JsonValueKind.Object &&
                infoEl.TryGetProperty("type", out var typeEl) &&
                typeEl.ValueKind == JsonValueKind.String &&
                string.Equals(typeEl.GetString(), "folder", StringComparison.OrdinalIgnoreCase) &&
                el.TryGetProperty("items", out var nestedItems) &&
                nestedItems.ValueKind == JsonValueKind.Array)
            {
                CollectHttpRequests(nestedItems, envId, list);
            }
        }
    }

    private SavedApiRequestItem? TryDeserializeRequest(string json, string envId)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<OpenCollectionRequestDoc>(json, SerializerOptions);
            if (doc?.Http is null) return null;

            var name = doc.Info?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"{doc.Http.Method} {doc.Http.Url}";
            }

            var headers = doc.Http.Headers?.Where(h => !string.IsNullOrWhiteSpace(h.Name))
                .ToDictionary(h => h.Name, h => h.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            return new SavedApiRequestItem
            {
                Id = string.Empty,
                EnvId = envId,
                Name = name,
                Method = doc.Http.Method ?? "POST",
                Url = doc.Http.Url ?? string.Empty,
                BodyJson = doc.Http.Body?.Data,
                Headers = headers
            };
        }
        catch
        {
            return null;
        }
    }

    private static SavedApiRequestItem Deserialize(SavedApiRequestRecord record)
    {
        // Prefer stored scalar columns; OpenCollectionJson is primarily for export/import compatibility.
        return new SavedApiRequestItem
        {
            Id = record.Id,
            EnvId = record.EnvId,
            Name = record.Name,
            Method = record.Method,
            Url = record.Url,
            BodyJson = TryGetBodyFromOpenCollection(record.OpenCollectionJson),
            Headers = TryGetHeadersFromOpenCollection(record.OpenCollectionJson),
            CreatedUtc = record.CreatedUtc,
            UpdatedUtc = record.UpdatedUtc
        };
    }

    private static string? TryGetBodyFromOpenCollection(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<OpenCollectionRequestDoc>(json, SerializerOptions);
            return doc?.Http?.Body?.Data;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string>? TryGetHeadersFromOpenCollection(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<OpenCollectionRequestDoc>(json, SerializerOptions);
            var headers = doc?.Http?.Headers;
            if (headers is null || headers.Count == 0) return null;
            return headers
                .Where(h => !string.IsNullOrWhiteSpace(h.Name))
                .ToDictionary(h => h.Name, h => h.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class SavedApiRequestItem
{
    public string Id { get; set; } = string.Empty;
    public string EnvId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Url { get; set; } = string.Empty;
    public string? BodyJson { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? CreatedUtc { get; set; }
    public string? UpdatedUtc { get; set; }
}
