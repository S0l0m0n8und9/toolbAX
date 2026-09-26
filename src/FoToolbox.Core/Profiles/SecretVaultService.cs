using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace FoToolbox.Core.Profiles;

/// <summary>An immutable DPAPI-protected vault row prepared before a profile transaction begins.</summary>
public sealed class ProtectedSecretRow
{
    private readonly byte[] _ciphertext;
    internal ProtectedSecretRow(string id, string kind, byte[] ciphertext)
    {
        Id = id;
        Kind = kind;
        _ciphertext = ciphertext;
    }
    public string Id { get; }
    public string Kind { get; }
    public ReadOnlyMemory<byte> Ciphertext => _ciphertext;
}

/// <summary>
/// DPAPI-backed secret vault stored in SQLite. Windows-only (DPAPI); non-Windows hosts must use a
/// platform-appropriate secret store.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecretVaultService
{
    private readonly string _connectionString;

    public SecretVaultService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<string> StoreSecretAsync<T>(string kind, T payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = PrepareSecret(kind, payload);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO SecretVault(Id, Kind, Blob) VALUES ($id, $kind, $blob)";
        cmd.Parameters.AddWithValue("$id", prepared.Id);
        cmd.Parameters.AddWithValue("$kind", prepared.Kind);
        cmd.Parameters.Add("$blob", SqliteType.Blob).Value = prepared.Ciphertext.ToArray();
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return prepared.Id;
    }

    /// <summary>Serializes and protects a secret without writing it to the database.</summary>
    public ProtectedSecretRow PrepareSecret<T>(string kind, T payload)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A secret kind is required.", nameof(kind));
        byte[]? plaintext = null;
        try
        {
            var json = JsonSerializer.Serialize(payload);
            plaintext = Encoding.UTF8.GetBytes(json);
            var ciphertext = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            return new ProtectedSecretRow(Guid.NewGuid().ToString("N"), kind, ciphertext);
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<T?> ReadSecretAsync<T>(string id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Blob FROM SecretVault WHERE Id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return default;
        }

        var blob = (byte[])reader["Blob"];
        var plaintext = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
        try
        {
            var json = Encoding.UTF8.GetString(plaintext);
            return JsonSerializer.Deserialize<T>(json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
