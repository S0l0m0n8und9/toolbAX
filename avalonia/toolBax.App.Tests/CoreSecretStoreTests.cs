using System;
using System.IO;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.App.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Tests the real <see cref="CoreSecretStore"/> over a temp profile DB. HasSecret/ClearSecret track
/// the F&amp;O service principal's SecretRef (no DPAPI → run on Linux CI); the SetSecret round-trip
/// exercises the DPAPI vault and is skipped off Windows.
/// </summary>
public sealed class CoreSecretStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"toolbax-sec-{Guid.NewGuid():N}.db");

    private ProfileService NewService() => new(new ProfileStore(_dbPath));

    // The vault construction is DPAPI/Windows-only (CA1416); HasSecret/ClearSecret never touch it,
    // so a null vault is fine for those cross-platform tests. SetSecret is Windows-guarded below.
    private SecretVaultService? NewVault() =>
        OperatingSystem.IsWindows() ? new SecretVaultService($"Data Source={_dbPath}") : null;

    private async Task SeedFoSpAsync(string envId, string? secretRef)
    {
        var svc = NewService();
        await svc.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await svc.UpsertEnvironmentAsync(new FoEnvironment(envId, "Env", "https://e", "t", null), TestContext.Current.CancellationToken);
        await svc.UpsertServicePrincipalAsync(
            new ServicePrincipal($"{envId}:fo", envId, "client-id", AuthMode.ClientSecret, secretRef, null, AuthTarget.Fo),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Has_secret_is_false_without_a_ref_and_true_with_one()
    {
        await SeedFoSpAsync("env1", secretRef: null);
        var store = new CoreSecretStore(NewService(), NewVault()!);
        Assert.False(store.HasSecret("env1"));

        await SeedFoSpAsync("env2", secretRef: "vault-uuid-123");
        Assert.True(new CoreSecretStore(NewService(), NewVault()!).HasSecret("env2"));
    }

    [Fact]
    public async Task Has_secret_is_false_when_no_service_principal()
    {
        var svc = NewService();
        await svc.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var store = new CoreSecretStore(svc, NewVault()!);

        Assert.False(store.HasSecret("missing"));
    }

    [Fact]
    public async Task Clear_secret_nulls_the_service_principal_ref()
    {
        await SeedFoSpAsync("env1", secretRef: "vault-uuid-123");
        var store = new CoreSecretStore(NewService(), NewVault()!);

        store.ClearSecret("env1");

        Assert.False(store.HasSecret("env1"));
        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, TestContext.Current.CancellationToken);
        Assert.Null(sp!.SecretRef);
    }

    [Fact]
    public async Task Set_secret_stores_in_the_vault_and_records_the_ref()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "SecretVaultService uses DPAPI (Windows-only).");

        await SeedFoSpAsync("env1", secretRef: null);
        var store = new CoreSecretStore(NewService(), NewVault()!);

        store.SetSecret("env1", "super-secret");

        Assert.True(store.HasSecret("env1"));
        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(sp!.SecretRef)); // a vault ref was recorded
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }
}
