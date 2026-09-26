using System;
using System.Collections.Generic;
using FoToolbox.Core.Models;
using Microsoft.Data.Sqlite;

namespace FoToolbox.Core.Profiles;

/// <summary>Typed, callback-scoped access to one profile database write transaction.</summary>
public sealed class ProfileMutationSession
{
    private const string DefaultEnvironmentKey = "DefaultEnvId";
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly CancellationToken _cancellationToken;
    private readonly int _ownerThreadId;
    private bool _active = true;

    internal ProfileMutationSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        _connection = connection;
        _transaction = transaction;
        _cancellationToken = cancellationToken;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public FoEnvironment? GetEnvironment(string id)
    {
        using var command = Command(@"SELECT Id, Name, BaseUrl, TenantId, DefaultCompany
FROM Environments WHERE Id = $id LIMIT 1");
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEnvironment(reader) : null;
    }

    public IReadOnlyList<FoEnvironment> GetEnvironments()
    {
        using var command = Command("SELECT Id, Name, BaseUrl, TenantId, DefaultCompany FROM Environments");
        using var reader = command.ExecuteReader();
        var result = new List<FoEnvironment>();
        while (reader.Read()) result.Add(ReadEnvironment(reader));
        return result;
    }

    public DataverseEnvironment? GetDataverseEnvironment(string environmentId)
    {
        using var command = Command("SELECT CeBaseUrl, CeTenantId FROM Environments WHERE Id = $id LIMIT 1");
        command.Parameters.AddWithValue("$id", environmentId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new DataverseEnvironment(
            environmentId,
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
    }

    public ServicePrincipal? GetServicePrincipal(string environmentId, AuthTarget target)
    {
        using var command = Command(@"SELECT Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint, Target
FROM ServicePrincipals WHERE EnvId = $env AND Target = $target LIMIT 1");
        command.Parameters.AddWithValue("$env", environmentId);
        command.Parameters.AddWithValue("$target", target.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPrincipal(reader) : null;
    }

    public IReadOnlyList<ServicePrincipal> GetServicePrincipals(string environmentId)
    {
        using var command = Command(@"SELECT Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint, Target
FROM ServicePrincipals WHERE EnvId = $env");
        command.Parameters.AddWithValue("$env", environmentId);
        using var reader = command.ExecuteReader();
        var result = new List<ServicePrincipal>();
        while (reader.Read()) result.Add(ReadPrincipal(reader));
        return result;
    }

    public string? GetSetting(string key)
    {
        using var command = Command("SELECT Value FROM Settings WHERE Key = $key LIMIT 1");
        command.Parameters.AddWithValue("$key", key);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : value.ToString();
    }

    public string? GetDefaultEnvironmentId() => GetSetting(DefaultEnvironmentKey);

    public void UpsertEnvironment(FoEnvironment environment)
    {
        using var command = Command(@"INSERT INTO Environments(Id, Name, BaseUrl, TenantId, DefaultCompany)
VALUES($id, $name, $base, $tenant, $company)
ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, BaseUrl=excluded.BaseUrl,
TenantId=excluded.TenantId, DefaultCompany=excluded.DefaultCompany");
        command.Parameters.AddWithValue("$id", environment.Id);
        command.Parameters.AddWithValue("$name", environment.Name);
        command.Parameters.AddWithValue("$base", environment.BaseUrl);
        command.Parameters.AddWithValue("$tenant", environment.TenantId);
        command.Parameters.AddWithValue("$company", (object?)environment.DefaultCompany ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void UpsertDataverseEnvironment(DataverseEnvironment environment)
    {
        using var command = Command(@"UPDATE Environments SET CeBaseUrl=$base, CeTenantId=$tenant WHERE Id=$id");
        command.Parameters.AddWithValue("$id", environment.ProfileId);
        command.Parameters.AddWithValue("$base", string.IsNullOrWhiteSpace(environment.BaseUrl) ? DBNull.Value : environment.BaseUrl);
        command.Parameters.AddWithValue("$tenant", string.IsNullOrWhiteSpace(environment.TenantId) ? DBNull.Value : environment.TenantId);
        command.ExecuteNonQuery();
    }

    public void UpsertServicePrincipal(ServicePrincipal principal)
    {
        using var command = Command(@"INSERT INTO ServicePrincipals(Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint, Target)
VALUES($id, $env, $client, $mode, $secret, $thumb, $target)
ON CONFLICT(EnvId, Target) DO UPDATE SET ClientId=excluded.ClientId,
AuthMode=excluded.AuthMode, SecretRef=excluded.SecretRef, CertThumbprint=excluded.CertThumbprint");
        command.Parameters.AddWithValue("$id", principal.Id);
        command.Parameters.AddWithValue("$env", principal.EnvId);
        command.Parameters.AddWithValue("$client", principal.ClientId);
        command.Parameters.AddWithValue("$mode", principal.AuthMode.ToString());
        command.Parameters.AddWithValue("$secret", (object?)principal.SecretRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$thumb", (object?)principal.CertThumbprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$target", principal.Target.ToString());
        command.ExecuteNonQuery();
    }

    public void DeleteServicePrincipal(string id)
    {
        using var command = Command("DELETE FROM ServicePrincipals WHERE Id=$id");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteEnvironment(string id)
    {
        using var command = Command("DELETE FROM Environments WHERE Id=$id");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetSetting(string key, string? value)
    {
        if (value is null)
        {
            using var delete = Command("DELETE FROM Settings WHERE Key=$key");
            delete.Parameters.AddWithValue("$key", key);
            delete.ExecuteNonQuery();
            return;
        }
        using var command = Command(@"INSERT INTO Settings(Key, Value) VALUES($key, $value)
ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value");
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public void SetDefaultEnvironment(string? environmentId) =>
        SetSetting(DefaultEnvironmentKey, environmentId);

    public void InsertProtectedSecret(ProtectedSecretRow secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        using var command = Command("INSERT INTO SecretVault(Id, Kind, Blob) VALUES($id, $kind, $blob)");
        command.Parameters.AddWithValue("$id", secret.Id);
        command.Parameters.AddWithValue("$kind", secret.Kind);
        command.Parameters.Add("$blob", SqliteType.Blob).Value = secret.Ciphertext.ToArray();
        command.ExecuteNonQuery();
    }

    public bool DeleteSecretIfUnreferenced(string id)
    {
        using (var references = Command(@"SELECT EXISTS(
SELECT 1 FROM ServicePrincipals WHERE SecretRef=$id
UNION ALL SELECT 1 FROM Settings WHERE Value=$id)") )
        {
            references.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt64(references.ExecuteScalar()) != 0) return false;
        }
        using var delete = Command("DELETE FROM SecretVault WHERE Id=$id");
        delete.Parameters.AddWithValue("$id", id);
        return delete.ExecuteNonQuery() > 0;
    }

    internal void EndScope() => _active = false;

    private SqliteCommand Command(string sql)
    {
        EnsureActive();
        _cancellationToken.ThrowIfCancellationRequested();
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }

    private void EnsureActive()
    {
        if (!_active) throw new InvalidOperationException("This profile mutation session is no longer active.");
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Profile mutation sessions may only be used by their owning synchronous callback thread.");
    }

    private static FoEnvironment ReadEnvironment(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4));

    private static ServicePrincipal ReadPrincipal(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2),
        Enum.Parse<AuthMode>(reader.GetString(3)),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) || !Enum.TryParse<AuthTarget>(reader.GetString(6), true, out var target)
            ? AuthTarget.Fo : target);
}
