using Microsoft.Data.Sqlite;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Catalog;

public sealed class CatalogStore
{
    private readonly string _connectionString;

    public CatalogStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS CatalogData(
  EnvId TEXT NOT NULL,
  Kind TEXT NOT NULL,
  Version TEXT NOT NULL,
  PayloadJson TEXT NOT NULL,
  ETag TEXT NULL,
  UpdatedUtc TEXT NOT NULL,
  PRIMARY KEY(EnvId, Kind)
);";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CatalogRecord?> GetAsync(string envId, string kind, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Version, PayloadJson, ETag, UpdatedUtc FROM CatalogData WHERE EnvId = $env AND Kind = $kind LIMIT 1";
        cmd.Parameters.AddWithValue("$env", envId);
        cmd.Parameters.AddWithValue("$kind", kind);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var version = reader.GetString(0);
            var json = reader.GetString(1);
            var etag = reader.IsDBNull(2) ? null : reader.GetString(2);
            var updated = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind);
            return new CatalogRecord(version, json, etag, updated);
        }

        return null;
    }

    public async Task SaveAsync(string envId, string kind, string version, string payloadJson, string? etag, DateTime updatedUtc, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO CatalogData(EnvId, Kind, Version, PayloadJson, ETag, UpdatedUtc)
VALUES($env, $kind, $version, $json, $etag, $updated)
ON CONFLICT(EnvId, Kind) DO UPDATE SET
 Version = excluded.Version,
 PayloadJson = excluded.PayloadJson,
 ETag = excluded.ETag,
 UpdatedUtc = excluded.UpdatedUtc;";
        cmd.Parameters.AddWithValue("$env", envId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$json", payloadJson);
        cmd.Parameters.AddWithValue("$etag", (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", updatedUtc.ToString("o"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTime> TouchAsync(string envId, string kind, CancellationToken cancellationToken = default)
    {
        var updatedUtc = DateTime.UtcNow;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CatalogData SET UpdatedUtc = $updated WHERE EnvId = $env AND Kind = $kind";
        cmd.Parameters.AddWithValue("$env", envId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$updated", updatedUtc.ToString("o"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updatedUtc;
    }
}

public sealed record CatalogRecord(string Version, string PayloadJson, string? ETag, DateTime UpdatedUtc);
