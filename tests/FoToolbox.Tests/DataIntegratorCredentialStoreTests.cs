using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FoToolbox.Tests;

public class DataIntegratorCredentialStoreTests
{
    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SaveThenGet_RoundTrips()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"di-{Guid.NewGuid():N}.db");
        try
        {
            var profiles = new ProfileStore(dbPath);
            await profiles.EnsureCreatedAsync();
            var vault = new SecretVaultService(profiles.ConnectionString);
            var store = new DataIntegratorCredentialStore(profiles, vault);

            await store.SaveAsync("env-1", new DataIntegratorCredential("2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b", "svc@contoso.com", "pw"), CancellationToken.None);
            var got = await store.GetAsync("env-1", CancellationToken.None);

            Assert.NotNull(got);
            Assert.Equal("svc@contoso.com", got!.Username);
            Assert.Equal("pw", got.Password);
            Assert.Null(await store.GetAsync("env-2", CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SaveTwice_DeletesPriorVaultEntry()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"di-{Guid.NewGuid():N}.db");
        try
        {
            var profiles = new ProfileStore(dbPath);
            await profiles.EnsureCreatedAsync();
            var vault = new SecretVaultService(profiles.ConnectionString);
            var store = new DataIntegratorCredentialStore(profiles, vault);

            await store.SaveAsync("env-1", new DataIntegratorCredential("c", "svc@contoso.com", "pw1"));
            await store.SaveAsync("env-1", new DataIntegratorCredential("c", "svc@contoso.com", "pw2"));

            var got = await store.GetAsync("env-1");
            Assert.Equal("pw2", got!.Password);

            await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(profiles.ConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM SecretVault";
                var count = (long)(await cmd.ExecuteScalarAsync())!;
                Assert.Equal(1L, count);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(dbPath); }
    }
}
