using FoToolbox.Core.Auth;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class VaultSecretReaderTests
{
    private static async Task<SecretVaultService> NewVaultAsync()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();
        return new SecretVaultService(store.ConnectionString);
    }

    private static async Task<(SecretVaultService Vault, string ConnectionString)> NewVaultWithConnectionAsync()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();
        return (new SecretVaultService(store.ConnectionString), store.ConnectionString);
    }

    /// <summary>
    /// Overwrites a stored blob with bytes DPAPI cannot unprotect — the same failure a profile.db
    /// restored onto another machine or Windows user account produces (CryptographicException:
    /// "Key not valid for use in specified state.").
    /// </summary>
    private static async Task CorruptBlobAsync(string connectionString, string secretRef)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE SecretVault SET Blob = $blob WHERE Id = $id";
        cmd.Parameters.Add("$blob", SqliteType.Blob).Value = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
        cmd.Parameters.AddWithValue("$id", secretRef);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Reads_Typed_ClientSecretPayload()
    {
        var vault = await NewVaultAsync();
        var secretRef = await vault.StoreSecretAsync("ClientSecret", new ClientSecretPayload { Value = "s3cret" });
        Assert.Equal("s3cret", await VaultSecretReader.ReadClientSecretAsync(vault, secretRef, default));
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Reads_Raw_String_Secret_Avalonia_Shape()
    {
        var vault = await NewVaultAsync();
        var secretRef = await vault.StoreSecretAsync("fo-client-secret", "s3cret");
        Assert.Equal("s3cret", await VaultSecretReader.ReadClientSecretAsync(vault, secretRef, default));
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Reads_BearerTokenPayload()
    {
        var vault = await NewVaultAsync();
        var secretRef = await vault.StoreSecretAsync("BearerToken", new BearerTokenPayload { AccessToken = "abc.def.ghi" });
        var payload = await VaultSecretReader.ReadBearerTokenAsync(vault, secretRef, default);
        Assert.Equal("abc.def.ghi", payload?.AccessToken);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Returns_Null_For_Missing_Ref()
    {
        var vault = await NewVaultAsync();
        Assert.Null(await VaultSecretReader.ReadClientSecretAsync(vault, Guid.NewGuid().ToString(), default));
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Reads_Raw_Bearer_Token_Fallback()
    {
        var vault = await NewVaultAsync();
        var secretRef = await vault.StoreSecretAsync("bearer", "abc.def.ghi");
        var payload = await VaultSecretReader.ReadBearerTokenAsync(vault, secretRef, default);
        Assert.Equal("abc.def.ghi", payload?.AccessToken);
        Assert.Null(payload?.ExpiresUtc);
    }

    // -----------------------------------------------------------------------
    // #168 low: an undecryptable blob must read as "no value", not throw past the env-var fallback
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Undecryptable_ClientSecret_Blob_Returns_Null_Instead_Of_Throwing()
    {
        var (vault, connectionString) = await NewVaultWithConnectionAsync();
        var secretRef = await vault.StoreSecretAsync("ClientSecret", new ClientSecretPayload { Value = "s3cret" });
        await CorruptBlobAsync(connectionString, secretRef);

        // Only JsonException was caught before, so DPAPI's CryptographicException escaped the reader,
        // escaped AuthBroker.ResolveStoredCredentialAsync ahead of its FOTB_CLIENT_SECRET fallback, and
        // surfaced as the raw "Key not valid for use in specified state." after three retries.
        Assert.Null(await VaultSecretReader.ReadClientSecretAsync(vault, secretRef, default));
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Undecryptable_BearerToken_Blob_Returns_Null_Instead_Of_Throwing()
    {
        var (vault, connectionString) = await NewVaultWithConnectionAsync();
        var secretRef = await vault.StoreSecretAsync("BearerToken", new BearerTokenPayload { AccessToken = "abc.def.ghi" });
        await CorruptBlobAsync(connectionString, secretRef);

        Assert.Null(await VaultSecretReader.ReadBearerTokenAsync(vault, secretRef, default));
    }
}
