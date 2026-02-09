using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.OData;

/// <summary>
/// SQLite-backed metadata cache keyed by environment.
/// </summary>
public sealed class ODataMetadataCache
{
    private readonly string _connectionString;

    public ODataMetadataCache(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS MetadataCache(
  EnvId TEXT PRIMARY KEY,
  ETag TEXT NULL,
  RawXml TEXT NOT NULL,
  UpdatedUtc TEXT NOT NULL
);";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ODataMetadataCacheEntry?> GetEntryAsync(string envId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ETag, RawXml, UpdatedUtc FROM MetadataCache WHERE EnvId = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", envId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var etag = reader.IsDBNull(0) ? null : reader.GetString(0);
        var xml = reader.GetString(1);
        var updatedText = reader.IsDBNull(2) ? null : reader.GetString(2);
        var updatedUtc = DateTime.TryParse(updatedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.MinValue;
        return new ODataMetadataCacheEntry(etag, xml, updatedUtc);
    }

    public async Task<(string ETag, string RawXml)?> GetAsync(string envId, CancellationToken cancellationToken = default)
    {
        var entry = await GetEntryAsync(envId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        return (entry.ETag ?? string.Empty, entry.RawXml);
    }

    public async Task SaveAsync(string envId, string? etag, string rawXml, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO MetadataCache(EnvId, ETag, RawXml, UpdatedUtc)
VALUES($id, $etag, $xml, $ts)
ON CONFLICT(EnvId) DO UPDATE SET
 ETag = excluded.ETag,
 RawXml = excluded.RawXml,
 UpdatedUtc = excluded.UpdatedUtc;";
        cmd.Parameters.AddWithValue("$id", envId);
        cmd.Parameters.AddWithValue("$etag", (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$xml", rawXml);
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DateTime> TouchAsync(string envId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE MetadataCache SET UpdatedUtc = $ts WHERE EnvId = $id";
        cmd.Parameters.AddWithValue("$ts", now.ToString("o"));
        cmd.Parameters.AddWithValue("$id", envId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return now;
    }
}

public sealed record ODataMetadataCacheEntry(string? ETag, string RawXml, DateTime UpdatedUtc);
