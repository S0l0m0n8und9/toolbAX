using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class SecretVaultTests
{
    [Fact]
    public async Task Secret_Roundtrips_With_Protection()
    {
        var db = Path.GetTempFileName();
        var store = new FoToolbox.Core.Profiles.ProfileStore(db);
        await store.EnsureCreatedAsync();

        var vault = new SecretVaultService(store.ConnectionString);
        var payload = new SecretPayload("super-secret");
        var id = await vault.StoreSecretAsync("ClientSecret", payload);

        var read = await vault.ReadSecretAsync<SecretPayload>(id);
        Assert.NotNull(read);
        Assert.Equal("super-secret", read!.Value);

        // Encryption-at-rest: the persisted blob must NOT contain the plaintext secret. Without this the
        // test would still pass if DPAPI Protect/Unprotect were dropped and the secret stored in cleartext
        // — which is the exact regression this vault exists to prevent. (Latin1 maps each byte 1:1 to a
        // char so the ASCII needle survives the cipher bytes.)
        var blob = ReadBlob(store.ConnectionString, id);
        Assert.NotNull(blob);
        Assert.DoesNotContain("super-secret", Encoding.Latin1.GetString(blob!));
    }

    private static byte[]? ReadBlob(string connectionString, string id)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Blob FROM SecretVault WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? (byte[])reader["Blob"] : null;
    }

    private sealed record SecretPayload(string Value);
}
