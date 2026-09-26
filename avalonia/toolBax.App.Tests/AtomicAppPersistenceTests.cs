using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

[Collection("Profile persistence SQLite")]
public sealed class AtomicAppPersistenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "toolbax-app-atomic-" + Guid.NewGuid().ToString("N") + ".db");
    private ProfileService Service => new(new ProfileStore(_path));
    private string ConnectionString => new ProfileStore(_path).ConnectionString;

    private static EnvProfile Profile(string name = "Original") => new(
        "env1", name, "https://fo.example", "tenant", "USMF", EnvProfile.NonProductionType,
        EnvStatus.Disconnected, DataverseUrl: "https://ce.example", ClientId: "legacy-client",
        AuthMode: FoAuthMode.Certificate);

    private async Task SeedLegacyAsync(string secretRef = "old-secret")
    {
        await Service.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await Service.UpsertEnvironmentAsync(new FoEnvironment(
            "env1", "Original", "https://fo.example", "tenant", "USMF"), TestContext.Current.CancellationToken);
        await Service.UpsertDataverseEnvironmentAsync(new DataverseEnvironment(
            "env1", "https://ce.example", "tenant"), TestContext.Current.CancellationToken);
        await Service.UpsertServicePrincipalAsync(new ServicePrincipal(
            "sp", "env1", "legacy-client", AuthMode.Certificate, secretRef, "thumb", AuthTarget.Fo),
            TestContext.Current.CancellationToken);
        await Service.SetSettingAsync("fo.authMode:env1", "Certificate", TestContext.Current.CancellationToken);
        await Service.SetSettingAsync("UnknownLegacy:env1", "future-value", TestContext.Current.CancellationToken);
        await Service.SetDefaultEnvironmentAsync("env1", TestContext.Current.CancellationToken);
        InsertVaultRow(secretRef);
    }

    [Theory]
    [InlineData("dataverse")]
    [InlineData("principal")]
    [InlineData("setting")]
    public async Task SaveAsync_late_failure_rolls_back_database_and_cache(string failurePoint)
    {
        await SeedLegacyAsync();
        switch (failurePoint)
        {
            case "dataverse":
                CreateTrigger("FailCe", "BEFORE UPDATE OF CeBaseUrl ON Environments WHEN OLD.Id='env1'", "late CE failure");
                break;
            case "principal":
                CreateTrigger("FailPrincipal", "BEFORE UPDATE ON ServicePrincipals WHEN OLD.EnvId='env1'", "late principal failure");
                break;
            default:
                CreateTrigger("FailTier", "BEFORE INSERT ON Settings WHEN NEW.Key='env.type:env1'", "late tier failure");
                break;
        }
        IProfileStore store = await CoreProfileStore.CreateAsync(Service, TestContext.Current.CancellationToken);
        var replacement = Profile("Changed") with { AuthMode = FoAuthMode.ClientSecret };

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.SaveAsync(replacement, TestContext.Current.CancellationToken));

        Assert.Equal("Original", (await Service.GetEnvironmentsAsync(TestContext.Current.CancellationToken)).Single().Name);
        Assert.Equal("Original", store.GetAll().Single().Name);
        Assert.Equal("future-value", await Service.GetSettingAsync("UnknownLegacy:env1", TestContext.Current.CancellationToken));
        Assert.Equal("old-secret", (await Service.GetServicePrincipalAsync("env1", AuthTarget.Fo, TestContext.Current.CancellationToken))!.SecretRef);
        Assert.Equal(1, CountVaultRows("old-secret"));
    }

    [Fact]
    public async Task DeleteAsync_blob_cleanup_failure_rolls_back_profile_default_and_cache()
    {
        await SeedLegacyAsync();
        CreateTrigger("FailBlobDelete", "BEFORE DELETE ON SecretVault WHEN OLD.Id='old-secret'", "blob cleanup failed");
        IProfileStore store = await CoreProfileStore.CreateAsync(Service, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.DeleteAsync("env1", TestContext.Current.CancellationToken));

        Assert.Single(await Service.GetEnvironmentsAsync(TestContext.Current.CancellationToken));
        Assert.Single(store.GetAll());
        Assert.Equal("env1", store.ActiveId);
        Assert.Equal("env1", await Service.GetDefaultEnvironmentIdAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, CountVaultRows("old-secret"));
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("pointer")]
    [InlineData("delete")]
    [SupportedOSPlatform("windows")]
    public async Task SetSecretAsync_failure_rolls_back_pointer_and_new_blob(string failurePoint)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI vault is Windows-only.");
        await SeedLegacyAsync();
        switch (failurePoint)
        {
            case "insert":
                CreateTrigger("FailBlobInsert", "BEFORE INSERT ON SecretVault", "blob insert failed");
                break;
            case "pointer":
                CreateTrigger("FailPointer", "BEFORE UPDATE OF SecretRef ON ServicePrincipals WHEN OLD.EnvId='env1'", "pointer failed");
                break;
            default:
                CreateTrigger("FailOldBlobDelete", "BEFORE DELETE ON SecretVault WHEN OLD.Id='old-secret'", "old blob cleanup failed");
                break;
        }
        ISecretStore secrets = new CoreSecretStore(Service, new SecretVaultService(ConnectionString));

        await Assert.ThrowsAsync<SqliteException>(() =>
            secrets.SetSecretAsync("env1", "replacement", SecretTarget.Fo, TestContext.Current.CancellationToken));

        Assert.Equal("old-secret", (await Service.GetServicePrincipalAsync("env1", AuthTarget.Fo, TestContext.Current.CancellationToken))!.SecretRef);
        Assert.Equal(1, CountVaultRows("old-secret"));
        Assert.Equal(1, CountAllVaultRows());
    }

    [Fact]
    public async Task ClearSecretAsync_blob_failure_rolls_back_pointer()
    {
        await SeedLegacyAsync();
        CreateTrigger("FailClearBlob", "BEFORE DELETE ON SecretVault WHEN OLD.Id='old-secret'", "clear failed");
        ISecretStore secrets = new CoreSecretStore(Service, OperatingSystem.IsWindows()
            ? new SecretVaultService(ConnectionString) : null);

        await Assert.ThrowsAsync<SqliteException>(() =>
            secrets.ClearSecretAsync("env1", SecretTarget.Fo, TestContext.Current.CancellationToken));

        Assert.Equal("old-secret", (await Service.GetServicePrincipalAsync(
            "env1", AuthTarget.Fo, TestContext.Current.CancellationToken))!.SecretRef);
        Assert.Equal(1, CountVaultRows("old-secret"));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Secret_rotation_preserves_an_old_blob_shared_by_another_profile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI vault is Windows-only.");
        await SeedLegacyAsync();
        await Service.UpsertEnvironmentAsync(new FoEnvironment(
            "env2", "Two", "https://two", "tenant", null), TestContext.Current.CancellationToken);
        await Service.UpsertServicePrincipalAsync(new ServicePrincipal(
            "sp2", "env2", "client2", AuthMode.ClientSecret, "old-secret", null, AuthTarget.Fo),
            TestContext.Current.CancellationToken);
        ISecretStore secrets = new CoreSecretStore(Service, new SecretVaultService(ConnectionString));

        await secrets.SetSecretAsync("env1", "replacement", SecretTarget.Fo, TestContext.Current.CancellationToken);

        Assert.Equal(1, CountVaultRows("old-secret"));
        Assert.NotEqual("old-secret", (await Service.GetServicePrincipalAsync(
            "env1", AuthTarget.Fo, TestContext.Current.CancellationToken))!.SecretRef);
    }

    [Fact]
    public async Task SetActiveAsync_failure_and_cancellation_leave_persisted_and_cached_active_unchanged()
    {
        await SeedLegacyAsync();
        await Service.UpsertEnvironmentAsync(new FoEnvironment(
            "env2", "Two", "https://two", "tenant", null), TestContext.Current.CancellationToken);
        CreateTrigger("FailDefault", "BEFORE UPDATE ON Settings WHEN OLD.Key='DefaultEnvId'", "default failed");
        IProfileStore store = await CoreProfileStore.CreateAsync(Service, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.SetActiveAsync("env2", TestContext.Current.CancellationToken));
        Assert.Equal("env1", store.ActiveId);
        Assert.Equal("env1", await Service.GetDefaultEnvironmentIdAsync(TestContext.Current.CancellationToken));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetActiveAsync("env2", cancelled.Token));
        Assert.Equal("env1", store.ActiveId);
    }

    [Fact]
    public async Task Serialized_SaveAsync_publication_keeps_last_commit_and_does_not_block_caller()
    {
        await SeedLegacyAsync();
        IProfileStore store = await CoreProfileStore.CreateAsync(Service, TestContext.Current.CancellationToken);
        using var blocker = new SqliteConnection(ConnectionString);
        blocker.Open();
        using var transaction = blocker.BeginTransaction(deferred: false);

        var first = store.SaveAsync(Profile("First"), TestContext.Current.CancellationToken);
        var second = store.SaveAsync(Profile("Second"), TestContext.Current.CancellationToken);
        await Task.Yield();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        transaction.Commit();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Second", store.GetAll().Single().Name);
        Assert.Equal("Second", (await Service.GetEnvironmentsAsync(TestContext.Current.CancellationToken)).Single(p => p.Id == "env1").Name);
    }

    [Fact]
    public async Task Cancellation_while_worker_waits_for_write_lock_rolls_back_and_keeps_cache()
    {
        await SeedLegacyAsync();
        IProfileStore store = await CoreProfileStore.CreateAsync(Service, TestContext.Current.CancellationToken);
        using var blocker = new SqliteConnection(ConnectionString);
        blocker.Open();
        using var transaction = blocker.BeginTransaction(deferred: false);
        using var cancellation = new CancellationTokenSource();

        var save = store.SaveAsync(Profile("Cancelled"), cancellation.Token);
        await Task.Yield();
        Assert.False(save.IsCompleted);
        cancellation.Cancel();
        transaction.Commit();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        Assert.Equal("Original", store.GetAll().Single().Name);
        Assert.Equal("Original", (await Service.GetEnvironmentsAsync(TestContext.Current.CancellationToken)).Single(p => p.Id == "env1").Name);
    }

    [Fact]
    public async Task GetAll_returns_a_read_only_snapshot_independent_of_later_publication()
    {
        await SeedLegacyAsync();
        IProfileStore store = await CoreProfileStore.CreateAsync(Service, TestContext.Current.CancellationToken);
        var snapshot = store.GetAll();

        await store.SaveAsync(Profile("Later"), TestContext.Current.CancellationToken);

        Assert.Equal("Original", snapshot.Single().Name);
        Assert.Equal("Later", store.GetAll().Single().Name);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<EnvProfile>)snapshot).Add(Profile("Injected")));
    }

    [Fact]
    public async Task DeleteAsync_preserves_a_different_persisted_default_and_unknown_settings()
    {
        await SeedLegacyAsync();
        await Service.UpsertEnvironmentAsync(new FoEnvironment(
            "env2", "Two", "https://two", "tenant", null), TestContext.Current.CancellationToken);
        await Service.SetDefaultEnvironmentAsync("env2", TestContext.Current.CancellationToken);
        IProfileStore store = await CoreProfileStore.CreateAsync(Service, TestContext.Current.CancellationToken);

        await store.DeleteAsync("env1", TestContext.Current.CancellationToken);

        Assert.Equal("env2", store.ActiveId);
        Assert.Equal("env2", await Service.GetDefaultEnvironmentIdAsync(TestContext.Current.CancellationToken));
        Assert.Equal("future-value", await Service.GetSettingAsync("UnknownLegacy:env1", TestContext.Current.CancellationToken));
        Assert.Single(store.GetAll(), profile => profile.Id == "env2");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task DataIntegrator_pointer_failure_rolls_back_new_blob_and_old_pointer()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI vault is Windows-only.");
        await Service.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await Service.UpsertEnvironmentAsync(new FoEnvironment(
            "env1", "One", "https://one", "tenant", null), TestContext.Current.CancellationToken);
        await Service.SetSettingAsync(CoreSecretStore.DiSecretRefSettingKey("env1"), "old-secret", TestContext.Current.CancellationToken);
        InsertVaultRow("old-secret");
        CreateTrigger("FailDiPointer", $"BEFORE UPDATE ON Settings WHEN OLD.Key='{CoreSecretStore.DiSecretRefSettingKey("env1")}'", "DI pointer failed");
        ISecretStore secrets = new CoreSecretStore(Service, new SecretVaultService(ConnectionString));

        await Assert.ThrowsAsync<SqliteException>(() => secrets.SetSecretAsync(
            "env1", "replacement", SecretTarget.DataIntegrator, TestContext.Current.CancellationToken));

        Assert.Equal("old-secret", await Service.GetSettingAsync(
            CoreSecretStore.DiSecretRefSettingKey("env1"), TestContext.Current.CancellationToken));
        Assert.Equal(1, CountAllVaultRows());
        Assert.True(await secrets.HasSecretAsync("env1", SecretTarget.DataIntegrator, TestContext.Current.CancellationToken));
    }

    private void CreateTrigger(string name, string timing, string message)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TRIGGER {name} {timing} BEGIN SELECT RAISE(ABORT,'{message}'); END;";
        command.ExecuteNonQuery();
    }

    private void InsertVaultRow(string id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SecretVault(Id, Kind, Blob) VALUES($id, 'test', X'010203')";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private int CountVaultRows(string id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SecretVault WHERE Id=$id";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private int CountAllVaultRows()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SecretVault";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
