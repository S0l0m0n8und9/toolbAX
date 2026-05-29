using FoToolbox.Core.Profiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Persists dual-write connection settings keyed by an arbitrary string (an environment id
/// for the Operations plugin, or "Left"/"Right" for Compare). The bearer token is
/// DPAPI-protected at rest. Stored under <c>%LocalAppData%/FoToolbox/</c>, mirroring the
/// plugin-local persistence pattern used elsewhere. Shared by dual-write plugins.
/// </summary>
public sealed class DualWriteConnectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ITokenProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    private Dictionary<string, DualWriteConnectionRecord> _items = new(StringComparer.OrdinalIgnoreCase);

    public DualWriteConnectionStore(string? path = null, ITokenProtector? protector = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? ProfilePaths.ResolveAppDataPath("dualwrite-connections.json")
            : path!;
        _protector = protector ?? new DpapiTokenProtector();
    }

    public async Task<DualWriteConnectionSettings> GetAsync(string key, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (_items.TryGetValue(key, out var record))
            {
                DateTimeOffset? expiry = null;
                if (!string.IsNullOrWhiteSpace(record.AccessTokenExpiryUtc) &&
                    DateTimeOffset.TryParse(record.AccessTokenExpiryUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                {
                    expiry = parsed;
                }

                return new DualWriteConnectionSettings(
                    key,
                    record.GatewayBaseUrl ?? string.Empty,
                    record.FoIdentifier ?? string.Empty,
                    string.IsNullOrEmpty(record.ProtectedToken) ? null : _protector.Unprotect(record.ProtectedToken!))
                {
                    RefreshToken = string.IsNullOrEmpty(record.ProtectedRefreshToken) ? null : _protector.Unprotect(record.ProtectedRefreshToken!),
                    AccessTokenExpiryUtc = expiry
                };
            }

            return new DualWriteConnectionSettings(key, string.Empty, string.Empty, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(DualWriteConnectionSettings settings, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _items[settings.Key] = new DualWriteConnectionRecord
            {
                Key = settings.Key,
                GatewayBaseUrl = settings.GatewayBaseUrl,
                FoIdentifier = settings.FoIdentifier,
                ProtectedToken = string.IsNullOrEmpty(settings.BearerToken) ? null : _protector.Protect(settings.BearerToken!),
                ProtectedRefreshToken = string.IsNullOrEmpty(settings.RefreshToken) ? null : _protector.Protect(settings.RefreshToken!),
                AccessTokenExpiryUtc = settings.AccessTokenExpiryUtc?.ToString("o"),
                UpdatedUtc = DateTime.UtcNow.ToString("o")
            };
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
            _items = new Dictionary<string, DualWriteConnectionRecord>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                _items = new Dictionary<string, DualWriteConnectionRecord>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var doc = JsonSerializer.Deserialize<DualWriteConnectionDocument>(json, SerializerOptions);
            _items = (doc?.Connections ?? new List<DualWriteConnectionRecord>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .ToDictionary(c => c.Key!, c => c, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _items = new Dictionary<string, DualWriteConnectionRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveUnlockedAsync(CancellationToken ct)
    {
        var doc = new DualWriteConnectionDocument
        {
            Connections = _items.Values
                .OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
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
}

internal sealed class DualWriteConnectionDocument
{
    public List<DualWriteConnectionRecord> Connections { get; set; } = new();
}

internal sealed class DualWriteConnectionRecord
{
    public string? Key { get; set; }
    public string? GatewayBaseUrl { get; set; }
    public string? FoIdentifier { get; set; }
    public string? ProtectedToken { get; set; }
    public string? ProtectedRefreshToken { get; set; }
    public string? AccessTokenExpiryUtc { get; set; }
    public string? UpdatedUtc { get; set; }
}
