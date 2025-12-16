using FoToolbox.Core.Profiles;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FoToolbox.Tests;

public class ProfileStoreSchemaTests
{
    [Fact]
    public async Task EnsureCreated_Builds_Schema()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();

        using var conn = new SqliteConnection(store.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Environments','Settings','ServicePrincipals','SecretVault','SavedQuery')";
        using var reader = cmd.ExecuteReader();

        var count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count);
    }
}
