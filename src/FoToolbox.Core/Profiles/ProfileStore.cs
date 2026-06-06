using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using Microsoft.Data.Sqlite;

namespace FoToolbox.Core.Profiles;

/// <summary>
/// SQLite-backed profile store for environments, service principals, and saved queries.
/// </summary>
public sealed class ProfileStore
{
    private readonly string _connectionString;

    public ProfileStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public string ConnectionString => _connectionString;

    /// <summary>Current schema version. Increment when adding a new migration.</summary>
    internal const int LatestSchemaVersion = 1;

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // The SchemaVersion table is always created unconditionally so we can track migrations.
        await using (var bootstrap = conn.CreateCommand())
        {
            bootstrap.CommandText = "CREATE TABLE IF NOT EXISTS SchemaVersion(Version INTEGER NOT NULL)";
            await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        }

        var current = await GetSchemaVersionAsync(conn, cancellationToken);

        // Run each migration that hasn't been applied yet, inside a transaction.
        if (current < LatestSchemaVersion)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            if (current < 0) await MigrateV0Async(conn, cancellationToken);
            if (current < 1) await MigrateV1Async(conn, cancellationToken);

            await SetSchemaVersionAsync(conn, LatestSchemaVersion, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
    }

    /// <summary>V0: Base schema - core tables.</summary>
    private static async Task MigrateV0Async(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS Environments(
  Id TEXT PRIMARY KEY,
  Name TEXT NOT NULL,
  BaseUrl TEXT NOT NULL,
  TenantId TEXT NOT NULL,
  DefaultCompany TEXT NULL
);
CREATE TABLE IF NOT EXISTS Settings(
  Key TEXT PRIMARY KEY,
  Value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ServicePrincipals(
  Id TEXT PRIMARY KEY,
  EnvId TEXT NOT NULL REFERENCES Environments(Id) ON DELETE CASCADE,
  ClientId TEXT NOT NULL,
  AuthMode TEXT NOT NULL,
  SecretRef TEXT NULL,
  CertThumbprint TEXT NULL
);
CREATE TABLE IF NOT EXISTS SecretVault(
  Id TEXT PRIMARY KEY,
  Kind TEXT NOT NULL,
  Blob BLOB NOT NULL
);
CREATE TABLE IF NOT EXISTS SavedQuery(
  Id TEXT PRIMARY KEY,
  EnvId TEXT NOT NULL REFERENCES Environments(Id) ON DELETE CASCADE,
  Name TEXT NOT NULL,
  SpecJson TEXT NOT NULL,
  CrossCompany INTEGER NOT NULL,
  CreatedUtc TEXT NOT NULL,
  UpdatedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS SavedApiRequest(
  Id TEXT PRIMARY KEY,
  EnvId TEXT NOT NULL REFERENCES Environments(Id) ON DELETE CASCADE,
  Name TEXT NOT NULL,
  Method TEXT NOT NULL,
  Url TEXT NOT NULL,
  OpenCollectionJson TEXT NOT NULL,
  CreatedUtc TEXT NOT NULL,
  UpdatedUtc TEXT NOT NULL
);";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>V1: Add Dataverse columns, ServicePrincipal Target, dedup, and unique index.</summary>
    private static async Task MigrateV1Async(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnExistsAsync(conn, "Environments", "CeBaseUrl", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(conn, "Environments", "CeTenantId", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(conn, "ServicePrincipals", "Target", "TEXT NOT NULL DEFAULT 'Fo'", cancellationToken);

        await using (var normalize = conn.CreateCommand())
        {
            normalize.CommandText = "UPDATE ServicePrincipals SET Target = 'Fo' WHERE Target IS NULL OR trim(Target) = ''";
            await normalize.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var dedupe = conn.CreateCommand())
        {
            dedupe.CommandText = @"
DELETE FROM ServicePrincipals
WHERE rowid NOT IN (
    SELECT MIN(rowid)
    FROM ServicePrincipals
    GROUP BY EnvId, Target
);";
            await dedupe.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var idx = conn.CreateCommand())
        {
            idx.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS UX_ServicePrincipals_EnvId_Target ON ServicePrincipals(EnvId, Target)";
            await idx.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Version), -1) FROM SchemaVersion";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is long v ? (int)v : -1;
    }

    private static async Task SetSchemaVersionAsync(SqliteConnection conn, int version, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SchemaVersion; INSERT INTO SchemaVersion(Version) VALUES($v)";
        cmd.Parameters.AddWithValue("$v", version);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key LIMIT 1";
        cmd.Parameters.AddWithValue("$key", key);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : result.ToString();
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Settings(Key, Value)
VALUES($key, $value)
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertEnvironmentAsync(FoEnvironment env, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Environments(Id, Name, BaseUrl, TenantId, DefaultCompany)
VALUES($id, $name, $baseUrl, $tenant, $company)
ON CONFLICT(Id) DO UPDATE SET
 Name = excluded.Name,
 BaseUrl = excluded.BaseUrl,
 TenantId = excluded.TenantId,
 DefaultCompany = excluded.DefaultCompany;";
        cmd.Parameters.AddWithValue("$id", env.Id);
        cmd.Parameters.AddWithValue("$name", env.Name);
        cmd.Parameters.AddWithValue("$baseUrl", env.BaseUrl);
        cmd.Parameters.AddWithValue("$tenant", env.TenantId);
        cmd.Parameters.AddWithValue("$company", (object?)env.DefaultCompany ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DataverseEnvironment?> GetDataverseEnvironmentAsync(string envId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CeBaseUrl, CeTenantId FROM Environments WHERE Id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", envId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var baseUrl = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var tenantId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        return new DataverseEnvironment(envId, baseUrl, tenantId);
    }

    public async Task UpsertDataverseEnvironmentAsync(DataverseEnvironment env, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE Environments
SET CeBaseUrl = $baseUrl,
    CeTenantId = $tenant
WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", env.ProfileId);
        cmd.Parameters.AddWithValue("$baseUrl", string.IsNullOrWhiteSpace(env.BaseUrl) ? (object)DBNull.Value : env.BaseUrl);
        cmd.Parameters.AddWithValue("$tenant", string.IsNullOrWhiteSpace(env.TenantId) ? (object)DBNull.Value : env.TenantId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteEnvironmentAsync(string envId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Environments WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", envId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Removes a stored secret blob by its vault id (no-op if absent). Not DPAPI — plain delete.</summary>
    public async Task DeleteSecretAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SecretVault WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FoEnvironment>> GetEnvironmentsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<FoEnvironment>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, BaseUrl, TenantId, DefaultCompany FROM Environments";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FoEnvironment(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
            ));
        }

        return list;
    }

    public async Task DeleteServicePrincipalAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ServicePrincipals WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServicePrincipal>> GetServicePrincipalsAsync(string envId, CancellationToken cancellationToken = default)
    {
        var list = new List<ServicePrincipal>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint, Target FROM ServicePrincipals WHERE EnvId = $env";
        cmd.Parameters.AddWithValue("$env", envId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ServicePrincipal(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<AuthMode>(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? AuthTarget.Fo : ParseAuthTarget(reader.GetString(6))
            ));
        }

        return list;
    }

    public async Task<ServicePrincipal?> GetServicePrincipalAsync(string envId, AuthTarget target, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint, Target
FROM ServicePrincipals
WHERE EnvId = $env AND Target = $target
LIMIT 1";
        cmd.Parameters.AddWithValue("$env", envId);
        cmd.Parameters.AddWithValue("$target", target.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ServicePrincipal(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<AuthMode>(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? AuthTarget.Fo : ParseAuthTarget(reader.GetString(6))
        );
    }

    public async Task UpsertServicePrincipalAsync(ServicePrincipal sp, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ServicePrincipals(Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint, Target)
VALUES($id, $env, $client, $mode, $secret, $thumb, $target)
ON CONFLICT(EnvId, Target) DO UPDATE SET
 ClientId = excluded.ClientId,
 AuthMode = excluded.AuthMode,
 SecretRef = excluded.SecretRef,
 CertThumbprint = excluded.CertThumbprint;";
        cmd.Parameters.AddWithValue("$id", sp.Id);
        cmd.Parameters.AddWithValue("$env", sp.EnvId);
        cmd.Parameters.AddWithValue("$client", sp.ClientId);
        cmd.Parameters.AddWithValue("$mode", sp.AuthMode.ToString());
        cmd.Parameters.AddWithValue("$secret", (object?)sp.SecretRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$thumb", (object?)sp.CertThumbprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$target", sp.Target.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedQueryRecord>> GetSavedQueriesAsync(string envId, CancellationToken cancellationToken = default)
    {
        var list = new List<SavedQueryRecord>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EnvId, Name, SpecJson, CrossCompany, CreatedUtc, UpdatedUtc FROM SavedQuery WHERE EnvId = $env";
        cmd.Parameters.AddWithValue("$env", envId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SavedQueryRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4) != 0,
                reader.GetString(5),
                reader.GetString(6)
            ));
        }
        return list;
    }

    public async Task SaveQueryAsync(SavedQueryRecord record, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO SavedQuery(Id, EnvId, Name, SpecJson, CrossCompany, CreatedUtc, UpdatedUtc)
VALUES($id, $env, $name, $spec, $cc, $created, $updated)
ON CONFLICT(Id) DO UPDATE SET
 Name = excluded.Name,
 SpecJson = excluded.SpecJson,
 CrossCompany = excluded.CrossCompany,
 UpdatedUtc = excluded.UpdatedUtc;";
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$env", record.EnvId);
        cmd.Parameters.AddWithValue("$name", record.Name);
        cmd.Parameters.AddWithValue("$spec", record.SpecJson);
        cmd.Parameters.AddWithValue("$cc", record.CrossCompany ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", record.CreatedUtc);
        cmd.Parameters.AddWithValue("$updated", record.UpdatedUtc);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteQueryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SavedQuery WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedApiRequestRecord>> GetSavedApiRequestsAsync(string envId, CancellationToken cancellationToken = default)
    {
        var list = new List<SavedApiRequestRecord>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EnvId, Name, Method, Url, OpenCollectionJson, CreatedUtc, UpdatedUtc FROM SavedApiRequest WHERE EnvId = $env";
        cmd.Parameters.AddWithValue("$env", envId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SavedApiRequestRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)
            ));
        }
        return list;
    }

    public async Task SaveApiRequestAsync(SavedApiRequestRecord record, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO SavedApiRequest(Id, EnvId, Name, Method, Url, OpenCollectionJson, CreatedUtc, UpdatedUtc)
VALUES($id, $env, $name, $method, $url, $json, $created, $updated)
ON CONFLICT(Id) DO UPDATE SET
 Name = excluded.Name,
 Method = excluded.Method,
 Url = excluded.Url,
 OpenCollectionJson = excluded.OpenCollectionJson,
 UpdatedUtc = excluded.UpdatedUtc;";
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$env", record.EnvId);
        cmd.Parameters.AddWithValue("$name", record.Name);
        cmd.Parameters.AddWithValue("$method", record.Method);
        cmd.Parameters.AddWithValue("$url", record.Url);
        cmd.Parameters.AddWithValue("$json", record.OpenCollectionJson);
        cmd.Parameters.AddWithValue("$created", record.CreatedUtc);
        cmd.Parameters.AddWithValue("$updated", record.UpdatedUtc);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteApiRequestAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SavedApiRequest WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnExistsAsync(SqliteConnection conn, string table, string column, string definitionSql, CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({table})";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(1));
            }
        }

        if (existing.Contains(column))
        {
            return;
        }

        await using (var alter = conn.CreateCommand())
        {
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definitionSql}";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static AuthTarget ParseAuthTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AuthTarget.Fo;
        }

        return Enum.TryParse<AuthTarget>(value, ignoreCase: true, out var parsed)
            ? parsed
            : AuthTarget.Fo;
    }
}
