using FoToolbox.Core.Profiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DualWriteMapBrowserPlugin;

internal sealed class TestifyConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    private Dictionary<string, TestifyMapConfiguration> _items = new(StringComparer.OrdinalIgnoreCase);

    public TestifyConfigurationStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? ProfilePaths.ResolveAppDataPath("testify-configurations.json")
            : path!;
    }

    public async Task<TestifyMapConfiguration> GetOrCreateAsync(string envId, string mapId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var key = BuildKey(envId, mapId);
            if (_items.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var created = new TestifyMapConfiguration
            {
                EnvId = envId,
                MapId = mapId,
                UpdatedUtc = DateTime.UtcNow.ToString("o")
            };
            _items[key] = created;
            await SaveUnlockedAsync(ct).ConfigureAwait(false);
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(TestifyMapConfiguration config, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            config.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _items[BuildKey(config.EnvId, config.MapId)] = config;
            await SaveUnlockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_path))
        {
            _items = new Dictionary<string, TestifyMapConfiguration>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                _items = new Dictionary<string, TestifyMapConfiguration>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var doc = JsonSerializer.Deserialize<TestifyConfigDocument>(json, SerializerOptions);
            var loaded = (doc?.Configurations ?? new List<TestifyMapConfiguration>())
                .Select(NormalizeConfiguration)
                .ToList();
            _items = loaded
                .Where(c => !string.IsNullOrWhiteSpace(c.EnvId) && !string.IsNullOrWhiteSpace(c.MapId))
                .ToDictionary(c => BuildKey(c.EnvId, c.MapId), c => c, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _items = new Dictionary<string, TestifyMapConfiguration>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveUnlockedAsync(CancellationToken ct)
    {
        var doc = new TestifyConfigDocument
        {
            Configurations = _items.Values
                .OrderBy(v => v.EnvId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.MapId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var json = JsonSerializer.Serialize(doc, SerializerOptions);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(_path, json, ct).ConfigureAwait(false);
    }

    private static string BuildKey(string envId, string mapId) => $"{envId}|{mapId}";

    private static TestifyMapConfiguration NormalizeConfiguration(TestifyMapConfiguration cfg)
    {
        cfg.OmitCreateFields = cfg.OmitCreateFields is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(cfg.OmitCreateFields, StringComparer.OrdinalIgnoreCase);

        cfg.PreferredCreateValues = cfg.PreferredCreateValues is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(cfg.PreferredCreateValues, StringComparer.OrdinalIgnoreCase);

        var byCompany = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (cfg.PreferredCreateValuesByCompany is not null)
        {
            foreach (var pair in cfg.PreferredCreateValuesByCompany)
            {
                byCompany[pair.Key] = pair.Value is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(pair.Value, StringComparer.OrdinalIgnoreCase);
            }
        }

        cfg.PreferredCreateValuesByCompany = byCompany;

        if (cfg.CePollTimeoutSeconds <= 0)
        {
            cfg.CePollTimeoutSeconds = 5;
        }

        return cfg;
    }
}

internal sealed class TestifyConfigDocument
{
    public List<TestifyMapConfiguration> Configurations { get; set; } = new();
}

public sealed class TestifyMapConfiguration
{
    public string EnvId { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string UpdatedUtc { get; set; } = string.Empty;
    public HashSet<string> OmitCreateFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PreferredCreateValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> PreferredCreateValuesByCompany { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long, in seconds, to wait for a CE record count delta before timing out. Defaults to 5 seconds.
    /// </summary>
    public int CePollTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// When true, incomplete enum value-map coverage is treated as a warning rather than a blocking
    /// issue. Patch steps are generated only for the enum values that are mapped.
    /// </summary>
    public bool AllowPartialEnumCoverage { get; set; } = false;

    /// <summary>
    /// The run token (e.g. "TESTIFY20240101120000") from the last successful CREATE.
    /// Used to detect whether the test record still exists and can be reused.
    /// </summary>
    public string? LastRunToken { get; set; }

    /// <summary>
    /// The OData instance URL (e.g. ".../MyEntitys(key='value')") of the record created
    /// during the last Testify run. Null if no record has been created or if the record was cleaned up.
    /// </summary>
    public string? LastEntityInstanceUrl { get; set; }
}
