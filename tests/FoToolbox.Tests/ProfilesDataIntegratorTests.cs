using System;
using System.IO;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FoToolbox.Tests;

public class ProfilesDataIntegratorTests
{
    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SaveDataIntegrator_PersistsWithDefaultClientId()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dip-{Guid.NewGuid():N}.db");
        try
        {
            var profiles = new ProfileStore(dbPath);
            await profiles.EnsureCreatedAsync();
            var vault = new SecretVaultService(profiles.ConnectionString);
            var store = new DataIntegratorCredentialStore(profiles, vault);

            await store.SaveAsync("env-1", new DataIntegratorCredential(DualWriteAuthConstants.ClientId, "svc@contoso.com", "pw"));
            var got = await store.GetAsync("env-1");

            Assert.Equal(DualWriteAuthConstants.ClientId, got!.ClientId);
            Assert.Equal("2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b", got.ClientId);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(dbPath); }
    }
}
