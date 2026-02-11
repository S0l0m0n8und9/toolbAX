using FoToolbox.Core.Profiles;
using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace QueryBuilderPlugin;

internal sealed class SavedQueryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new FilterDtoJsonConverter() }
    };

    private static readonly JsonSerializerOptions OpenCollectionSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
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

    public string ExportAllAsOpenCollection(string collectionName, string baseUrl, IReadOnlyList<SavedQueryItem> items)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("BaseUrl is required.", nameof(baseUrl));

        var doc = new OpenCollectionCollectionDoc
        {
            OpenCollection = "1.0.0",
            Info = new OpenCollectionCollectionInfo { Name = string.IsNullOrWhiteSpace(collectionName) ? "FoToolbox Queries" : collectionName },
            Bundled = true,
            Items = items.Select((i, idx) => new OpenCollectionHttpRequestItem
            {
                Info = new OpenCollectionItemInfo { Name = i.Name, Type = "http", Seq = idx + 1 },
                Http = new OpenCollectionHttp
                {
                    Method = "GET",
                    Url = BuildQueryUrl(baseUrl, i),
                    Headers = new List<OpenCollectionHeader>
                    {
                        new OpenCollectionHeader { Name = "Accept", Value = "application/json" }
                    }
                }
            }).ToList()
        };

        return JsonSerializer.Serialize(doc, OpenCollectionSerializerOptions);
    }

    public IReadOnlyList<SavedQueryItem> ImportOpenCollection(string json, string envId, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<SavedQueryItem>();

        var list = new List<SavedQueryItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                CollectHttpRequests(itemsEl, envId, baseUrl, list);
                return list;
            }

            // Allow importing a single HttpRequest item.
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("http", out _))
            {
                var one = TryDeserializeRequest(root.GetRawText(), envId, baseUrl);
                if (one is not null) list.Add(one);
            }

            return list;
        }
        catch
        {
            return Array.Empty<SavedQueryItem>();
        }
    }

    private static void CollectHttpRequests(JsonElement itemsArray, string envId, string? baseUrl, List<SavedQueryItem> list)
    {
        foreach (var el in itemsArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;

            // HTTP request item
            if (el.TryGetProperty("http", out _))
            {
                var one = TryDeserializeRequest(el.GetRawText(), envId, baseUrl);
                if (one is not null) list.Add(one);
                continue;
            }

            // Folder items can contain nested items. (OpenCollection "Folder" type)
            if (el.TryGetProperty("info", out var infoEl) &&
                infoEl.ValueKind == JsonValueKind.Object &&
                infoEl.TryGetProperty("type", out var typeEl) &&
                typeEl.ValueKind == JsonValueKind.String &&
                string.Equals(typeEl.GetString(), "folder", StringComparison.OrdinalIgnoreCase) &&
                el.TryGetProperty("items", out var nestedItems) &&
                nestedItems.ValueKind == JsonValueKind.Array)
            {
                CollectHttpRequests(nestedItems, envId, baseUrl, list);
            }
        }
    }

    private static SavedQueryItem? TryDeserializeRequest(string json, string envId, string? baseUrl)
    {
        try
        {
            var req = JsonSerializer.Deserialize<OpenCollectionHttpRequestItem>(json, OpenCollectionSerializerOptions);
            if (req?.Http is null) return null;

            var method = (req.Http.Method ?? "GET").Trim().ToUpperInvariant();
            if (method != "GET") return null;

            if (!TryParseODataUrl(baseUrl, req.Http.Url, out var parsed))
            {
                return null;
            }

            var name = req.Info?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"GET {parsed.Entity}";
            }

            return new SavedQueryItem
            {
                EnvId = envId,
                Name = name,
                Entity = parsed.Entity,
                CrossCompany = parsed.CrossCompany,
                Company = parsed.Company,
                Select = parsed.Select,
                OrderBy = parsed.OrderBy,
                Top = parsed.Top,
                Skip = parsed.Skip,
                Count = parsed.Count,
                FilterText = parsed.Filter,
                Expand = parsed.Expand,
                FilterRoot = null
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed record ParsedODataQuery(
        string Entity,
        bool CrossCompany,
        string? Company,
        List<string> Select,
        string? OrderBy,
        int? Top,
        int? Skip,
        bool Count,
        string? Filter,
        string? Expand);

    private static bool TryParseODataUrl(string? baseUrl, string url, out ParsedODataQuery parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(url)) return false;

        Uri uri;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            uri = abs;
        }
        else if (!string.IsNullOrWhiteSpace(baseUrl) &&
                 Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) &&
                 Uri.TryCreate(baseUri, url, out var resolved))
        {
            uri = resolved;
        }
        else
        {
            return false;
        }

        var path = uri.AbsolutePath ?? string.Empty;
        var entity = ExtractEntityFromPath(path);
        if (string.IsNullOrWhiteSpace(entity)) return false;

        var q = ParseQueryString(uri.Query);

        var crossCompany = q.TryGetValue("cross-company", out var cc) &&
                           string.Equals(cc, "true", StringComparison.OrdinalIgnoreCase);

        var select = q.TryGetValue("$select", out var sel) && !string.IsNullOrWhiteSpace(sel)
            ? sel.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
            : new List<string>();

        var orderBy = q.TryGetValue("$orderby", out var ob) && !string.IsNullOrWhiteSpace(ob) ? ob : null;

        int? top = null;
        if (q.TryGetValue("$top", out var topStr) && int.TryParse(topStr, out var topVal)) top = topVal;

        int? skip = null;
        if (q.TryGetValue("$skip", out var skipStr) && int.TryParse(skipStr, out var skipVal)) skip = skipVal;

        var count = q.TryGetValue("$count", out var cStr) && string.Equals(cStr, "true", StringComparison.OrdinalIgnoreCase);
        var expand = q.TryGetValue("$expand", out var exStr) && !string.IsNullOrWhiteSpace(exStr) ? exStr : null;

        string? filter = q.TryGetValue("$filter", out var fStr) && !string.IsNullOrWhiteSpace(fStr) ? fStr : null;
        var company = (string?)null;
        if (!crossCompany && !string.IsNullOrWhiteSpace(filter))
        {
            if (TryExtractCompanyFromFilter(filter!, out var extractedCompany, out var remainingFilter))
            {
                company = extractedCompany;
                filter = remainingFilter;
            }
        }

        parsed = new ParsedODataQuery(entity, crossCompany, company, select, orderBy, top, skip, count, filter, expand);
        return true;
    }

    private static string ExtractEntityFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var idx = path.IndexOf("/data/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var s = path[(idx + "/data/".Length)..];
            var cut = s.IndexOfAny(new[] { '/', '(' });
            return cut >= 0 ? s[..cut] : s;
        }

        // Fallback: last segment.
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        var last = parts[^1];
        var cut2 = last.IndexOf('(');
        return cut2 >= 0 ? last[..cut2] : last;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return dict;

        var q = query.StartsWith("?", StringComparison.Ordinal) ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            var val = Uri.UnescapeDataString(part[(eq + 1)..]);
            if (!string.IsNullOrWhiteSpace(key))
            {
                dict[key] = val;
            }
        }
        return dict;
    }

    private static bool TryExtractCompanyFromFilter(string filter, out string company, out string? remainingFilter)
    {
        company = string.Empty;
        remainingFilter = filter;

        // dataAreaId eq 'USMF'
        var simple = "dataAreaId eq ";
        var trimmed = filter.Trim();
        if (trimmed.StartsWith(simple, StringComparison.OrdinalIgnoreCase))
        {
            var rhs = trimmed[simple.Length..].Trim();
            if (rhs.Length >= 2 && rhs[0] == '\'' && rhs[^1] == '\'')
            {
                company = rhs[1..^1].Replace("''", "'");
                remainingFilter = null;
                return true;
            }
        }

        // (dataAreaId eq 'USMF') and (<rest>)
        // Keep this intentionally conservative to avoid corrupting filters.
        var prefix = "(dataAreaId eq '";
        var mid = "') and (";
        var suffix = ")";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var midIdx = trimmed.IndexOf(mid, StringComparison.OrdinalIgnoreCase);
            if (midIdx > prefix.Length)
            {
                var compRaw = trimmed.Substring(prefix.Length, midIdx - prefix.Length);
                var rest = trimmed[(midIdx + mid.Length)..];
                if (rest.EndsWith(suffix, StringComparison.Ordinal))
                {
                    rest = rest[..^suffix.Length];
                    company = compRaw.Replace("''", "'");
                    remainingFilter = string.IsNullOrWhiteSpace(rest) ? null : rest;
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildQueryUrl(string baseUrl, SavedQueryItem item)
    {
        var filter = item.FilterText;
        if (string.IsNullOrWhiteSpace(filter) && item.FilterRoot is not null)
        {
            filter = RenderFilter(item.FilterRoot);
        }

        var spec = new QuerySpec(
            item.Entity,
            CrossCompany: item.CrossCompany,
            Company: item.Company,
            Select: item.Select,
            OrderBy: item.OrderBy,
            Top: item.Top,
            Skip: item.Skip,
            Expand: item.Expand,
            Filter: filter,
            Where: null,
            Count: item.Count);

        return QueryBuilder.Build(baseUrl, spec).Url;
    }

    private static string RenderFilter(FilterDto node)
    {
        return node switch
        {
            FilterConditionDto cond => RenderCondition(cond),
            FilterGroupDto grp => $"({string.Join($" {grp.LogicalOperator ?? "and"} ", (grp.Children ?? new List<FilterDto>()).Select(RenderFilter))})",
            _ => string.Empty
        };
    }

    private static string RenderCondition(FilterConditionDto cond)
    {
        var field = cond.Field ?? string.Empty;
        var op = cond.Operator ?? "eq";
        var value = cond.Value ?? string.Empty;

        if (op is "startswith" or "endswith" or "contains")
        {
            return $"{op}({field},{value})";
        }

        return $"{field} {op} {value}";
    }
}
