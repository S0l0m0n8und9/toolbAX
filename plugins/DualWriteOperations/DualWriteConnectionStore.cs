using FoToolbox.Core.Profiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DualWriteOperationsPlugin;

/// <summary>
/// Per-environment dual-write connection settings: the gateway base URL, the F&amp;O
/// identifier used to resolve the linkage, and the pasted bearer token (DPAPI-protected
/// at rest). Persisted to <c>%LocalAppData%/FoToolbox/dualwrite-connections.json</c>,
/// mirroring the plugin-local persistence pattern used by Testify configuration.
/// </summary>
internal sealed class DualWriteConnectionStore
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

    public async Task<DualWriteConnectionSettings> GetAsync(string envId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (_items.TryGetValue(envId, out var record))
            {
                return new DualWriteConnectionSettings(
                    envId,
                    record.GatewayBaseUrl ?? string.Empty,
                    record.FoIdentifier ?? string.Empty,
                    string.IsNullOrEmpty(record.ProtectedToken) ? null : _protector.Unprotect(record.ProtectedToken!));
            }

            return new DualWriteConnectionSettings(envId, string.Empty, string.Empty, null);
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
            _items[settings.EnvId] = new DualWriteConnectionRecord
            {
                EnvId = settings.EnvId,
                GatewayBaseUrl = settings.GatewayBaseUrl,
                FoIdentifier = settings.FoIdentifier,
                ProtectedToken = string.IsNullOrEmpty(settings.BearerToken) ? null : _protector.Protect(settings.BearerToken!),
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
                .Where(c => !string.IsNullOrWhiteSpace(c.EnvId))
                .ToDictionary(c => c.EnvId!, c => c, StringComparer.OrdinalIgnoreCase);
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
                .OrderBy(v => v.EnvId, StringComparer.OrdinalIgnoreCase)
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
    public string? EnvId { get; set; }
    public string? GatewayBaseUrl { get; set; }
    public string? FoIdentifier { get; set; }
    public string? ProtectedToken { get; set; }
    public string? UpdatedUtc { get; set; }
}

/// <summary>Decrypted, in-memory connection settings handed to the view-model.</summary>
internal sealed record DualWriteConnectionSettings(string EnvId, string GatewayBaseUrl, string FoIdentifier, string? BearerToken)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(GatewayBaseUrl) &&
        !string.IsNullOrWhiteSpace(FoIdentifier) &&
        !string.IsNullOrWhiteSpace(BearerToken);
}
