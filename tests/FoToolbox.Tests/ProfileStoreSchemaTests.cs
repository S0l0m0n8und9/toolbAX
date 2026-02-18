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
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Environments','Settings','ServicePrincipals','SecretVault','SavedQuery','SavedApiRequest')";
        var count = 0;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read()) count++;
        }
        Assert.Equal(6, count);

        cmd.CommandText = "PRAGMA table_info(Environments)";
        var envColumns = new System.Collections.Generic.List<string>();
        using (var envReader = cmd.ExecuteReader())
        {
            while (envReader.Read()) envColumns.Add(envReader.GetString(1));
        }
        Assert.Contains("CeBaseUrl", envColumns);
        Assert.Contains("CeTenantId", envColumns);

        cmd.CommandText = "PRAGMA table_info(ServicePrincipals)";
        var spColumns = new System.Collections.Generic.List<string>();
        using (var spReader = cmd.ExecuteReader())
        {
            while (spReader.Read()) spColumns.Add(spReader.GetString(1));
        }
        Assert.Contains("Target", spColumns);

        cmd.CommandText = "PRAGMA index_list('ServicePrincipals')";
        var indexNames = new System.Collections.Generic.List<string>();
        using (var indexReader = cmd.ExecuteReader())
        {
            while (indexReader.Read()) indexNames.Add(indexReader.GetString(1));
        }
        Assert.Contains("UX_ServicePrincipals_EnvId_Target", indexNames);
    }
}
