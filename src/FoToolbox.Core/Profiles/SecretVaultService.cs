using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace FoToolbox.Core.Profiles;

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
        var id = Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(payload);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var cipher = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO SecretVault(Id, Kind, Blob) VALUES ($id, $kind, $blob)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.Add("$blob", SqliteType.Blob).Value = cipher;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return id;
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
        var json = Encoding.UTF8.GetString(plaintext);
        return JsonSerializer.Deserialize<T>(json);
    }
}
