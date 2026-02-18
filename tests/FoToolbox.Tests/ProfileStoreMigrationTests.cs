using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class ProfileStoreMigrationTests
{
    [Fact]
    public async Task EnsureCreated_Migrates_Legacy_Schema_And_Backfills_Target()
    {
        var db = Path.GetTempFileName();
        using (var conn = new SqliteConnection($"Data Source={db};Foreign Keys=True"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
PRAGMA foreign_keys = ON;
CREATE TABLE Environments(
  Id TEXT PRIMARY KEY,
  Name TEXT NOT NULL,
  BaseUrl TEXT NOT NULL,
  TenantId TEXT NOT NULL,
  DefaultCompany TEXT NULL
);
CREATE TABLE ServicePrincipals(
  Id TEXT PRIMARY KEY,
  EnvId TEXT NOT NULL REFERENCES Environments(Id) ON DELETE CASCADE,
  ClientId TEXT NOT NULL,
  AuthMode TEXT NOT NULL,
  SecretRef TEXT NULL,
  CertThumbprint TEXT NULL
);
CREATE TABLE SavedQuery(
  Id TEXT PRIMARY KEY,
  EnvId TEXT NOT NULL REFERENCES Environments(Id) ON DELETE CASCADE,
  Name TEXT NOT NULL,
  SpecJson TEXT NOT NULL,
  CrossCompany INTEGER NOT NULL,
  CreatedUtc TEXT NOT NULL,
  UpdatedUtc TEXT NOT NULL
);
INSERT INTO Environments(Id, Name, BaseUrl, TenantId, DefaultCompany)
VALUES('env1', 'Env 1', 'https://contoso.operations.dynamics.com', 'tenant', 'USMF');
INSERT INTO ServicePrincipals(Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint)
VALUES('sp1', 'env1', 'client-1', 'ClientSecret', 'secret-1', NULL);
INSERT INTO ServicePrincipals(Id, EnvId, ClientId, AuthMode, SecretRef, CertThumbprint)
VALUES('sp2', 'env1', 'client-2', 'ClientSecret', 'secret-2', NULL);
INSERT INTO SavedQuery(Id, EnvId, Name, SpecJson, CrossCompany, CreatedUtc, UpdatedUtc)
VALUES('q1', 'env1', 'Query 1', '{""entity"":""CustCustomerV3Entity""}', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');";
            cmd.ExecuteNonQuery();
        }

        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();

        using var verifyConn = new SqliteConnection(store.ConnectionString);
        verifyConn.Open();
        using var verify = verifyConn.CreateCommand();

        verify.CommandText = "PRAGMA table_info(Environments)";
        var hasCeBaseUrl = false;
        var hasCeTenantId = false;
        using (var envCols = verify.ExecuteReader())
        {
            while (envCols.Read())
            {
                var name = envCols.GetString(1);
                if (name == "CeBaseUrl") hasCeBaseUrl = true;
                if (name == "CeTenantId") hasCeTenantId = true;
            }
        }
        Assert.True(hasCeBaseUrl);
        Assert.True(hasCeTenantId);

        verify.CommandText = "SELECT COUNT(*) FROM ServicePrincipals WHERE EnvId = 'env1'";
        var principalCount = (long)verify.ExecuteScalar()!;
        Assert.Equal(1, principalCount);

        verify.CommandText = "SELECT Target FROM ServicePrincipals WHERE EnvId = 'env1' LIMIT 1";
        var target = (string)verify.ExecuteScalar()!;
        Assert.Equal("Fo", target);

        verify.CommandText = "SELECT COUNT(*) FROM SavedQuery WHERE EnvId = 'env1'";
        var savedQueryCount = (long)verify.ExecuteScalar()!;
        Assert.Equal(1, savedQueryCount);

        verify.CommandText = "PRAGMA index_list('ServicePrincipals')";
        var hasTargetIndex = false;
        using (var idxReader = verify.ExecuteReader())
        {
            while (idxReader.Read())
            {
                if (idxReader.GetString(1) == "UX_ServicePrincipals_EnvId_Target")
                {
                    hasTargetIndex = true;
                    break;
                }
            }
        }
        Assert.True(hasTargetIndex);
    }
}
