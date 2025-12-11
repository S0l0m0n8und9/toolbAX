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

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS Environments(
  Id TEXT PRIMARY KEY,
  Name TEXT NOT NULL,
  BaseUrl TEXT NOT NULL,
  TenantId TEXT NOT NULL,
  DefaultCompany TEXT NULL
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
);";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
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

    public async Task<IReadOnlyList<ServicePrincipal>> GetServicePrincipalsAsync(string envId, CancellationToken cancellationToken = default)
    {
        var list = new List<ServicePrincipal>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint FROM ServicePrincipals WHERE EnvId = $env";
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
                reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }

        return list;
    }

    public async Task UpsertServicePrincipalAsync(ServicePrincipal sp, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ServicePrincipals(Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint)
VALUES($id, $env, $client, $mode, $secret, $thumb)
ON CONFLICT(Id) DO UPDATE SET
 EnvId = excluded.EnvId,
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
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
