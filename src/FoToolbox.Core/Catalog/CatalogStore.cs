using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Catalog;

public sealed class CatalogStore
{
    private readonly string _connectionString;
    private readonly MetadataRetentionCoordinator _retention;

    public CatalogStore(string databasePath)
    {
        _retention = MetadataRetentionCoordinator.ForDatabase(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    internal long MetadataPruneGeneration => _retention.PruneGeneration;

    internal ValueTask<IAsyncDisposable> LeaseMetadataPartitionAsync(string profileId, string key, CancellationToken ct) =>
        _retention.AcquireAsync(this, profileId, key, ct);

    private const string PartitionPrefix = "catalog-meta-v1:";
    private const string KnownMetadataKinds =
        "(Kind = $full OR Kind = $xml OR Kind = $index OR substr(Kind, 1, length($details)) = $details)";

    // Called only while the process-local admission gate is held. Reads summaries, never XML/payload
    // blobs, then atomically removes known representations of the oldest unleased groups. Deleted pages
    // are reusable by SQLite; this neither VACUUMs nor promises a fixed byte/file-size bound.
    internal async Task<bool> PruneMetadataPartitionsAsync(string profileId, IEnumerable<string> leasedKeys, CancellationToken ct)
    {
        var protectedKeys = leasedKeys.ToHashSet(StringComparer.Ordinal);
        var connectionString = new SqliteConnectionStringBuilder(_connectionString) { DefaultTimeout = 2 }.ToString();
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = conn.BeginTransaction();
        var groups = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var select = conn.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandTimeout = 2;
            // Parse the round-trip timestamps without SQLite julianday's sub-millisecond rounding.
            // These tiny summaries avoid loading the multi-megabyte XML/JSON payloads.
            select.CommandText = $"SELECT EnvId, UpdatedUtc FROM CatalogData WHERE substr(EnvId, 1, length($prefix)) = $prefix AND {KnownMetadataKinds}";
            select.Parameters.AddWithValue("$prefix", PartitionPrefix);
            AddMetadataKinds(select);
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                var key = reader.GetString(0);
                if (!protectedKeys.Contains(key) && IsPartitionForProfile(key, profileId)
                    && DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var updated))
                {
                    groups.TryGetValue(key, out var previous);
                    groups[key] = Math.Max(previous, updated.UtcTicks);
                }
            }
        }

        var removed = 0;
        foreach (var group in groups.OrderByDescending(g => g.Value).ThenBy(g => g.Key, StringComparer.Ordinal).Skip(3))
        {
            ct.ThrowIfCancellationRequested();
            await using var delete = conn.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandTimeout = 2;
            delete.CommandText = $"DELETE FROM CatalogData WHERE EnvId = $key COLLATE BINARY AND {KnownMetadataKinds}";
            delete.Parameters.AddWithValue("$key", group.Key);
            AddMetadataKinds(delete);
            removed += await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return removed > 0;
    }

    private static void AddMetadataKinds(SqliteCommand command)
    {
        command.Parameters.AddWithValue("$full", "ODataMetadata");
        command.Parameters.AddWithValue("$xml", "ODataMetadataXml");
        command.Parameters.AddWithValue("$index", "ODataEntityIndex");
        command.Parameters.AddWithValue("$details", "ODataEntityDetails:");
    }

    private static bool IsPartitionForProfile(string key, string profileId)
    {
        if (!key.StartsWith(PartitionPrefix, StringComparison.Ordinal)) return false;
        try
        {
            using var doc = JsonDocument.Parse(key[PartitionPrefix.Length..]);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 3
                && root.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)
                && string.Equals(root[0].GetString(), profileId, StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
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

    /// <summary>
    /// Inserts a catalog row only when no row exists for the same environment and kind. Callers use the
    /// boolean result to re-read the winner rather than overwriting a concurrent writer's newer payload.
    /// </summary>
    public async Task<bool> InsertIfAbsentAsync(string envId, string kind, string version, string payloadJson,
        string? etag, DateTime updatedUtc, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO CatalogData(EnvId, Kind, Version, PayloadJson, ETag, UpdatedUtc)
VALUES($env, $kind, $version, $json, $etag, $updated)
ON CONFLICT(EnvId, Kind) DO NOTHING;";
        cmd.Parameters.AddWithValue("$env", envId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$json", payloadJson);
        cmd.Parameters.AddWithValue("$etag", (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", updatedUtc.ToString("o"));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
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
