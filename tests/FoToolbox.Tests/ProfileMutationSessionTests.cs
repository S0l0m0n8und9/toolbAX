using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FoToolbox.Tests;

public sealed class ProfileMutationSessionTests
{
    private sealed record SecretPayload(string Value);

    private static FoEnvironment Environment(string id = "env-1", string name = "Original") =>
        new(id, name, $"https://{id}.operations.dynamics.com", "tenant", "USMF");

    private static async Task<(string Path, ProfileStore Store, SecretVaultService Vault)> NewStoreAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "toolbax-profile-mutation-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new ProfileStore(path);
        await store.EnsureCreatedAsync();
        return (path, store, new SecretVaultService(store.ConnectionString));
    }

    [Fact]
    public async Task Legacy_separate_calls_can_leave_a_partial_profile_after_late_failure()
    {
        var (_, store, _) = await NewStoreAsync();
        await store.UpsertEnvironmentAsync(Environment(), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await store.SetSettingAsync("LegacyMarker", "written", CancellationToken.None);
            throw new InvalidOperationException("late failure");
        });

        Assert.Single(await store.GetEnvironmentsAsync(CancellationToken.None));
        Assert.Equal("written", await store.GetSettingAsync("LegacyMarker", CancellationToken.None));
    }

    [Fact]
    public async Task Late_exception_rolls_back_environment_principals_settings_default_and_blob()
    {
        var (_, store, vault) = await NewStoreAsync();
        var protectedSecret = vault.PrepareSecret("ClientSecret", new SecretPayload("rollback-secret"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RunProfileMutationAsync<int>(session =>
        {
            session.UpsertEnvironment(Environment());
            session.UpsertDataverseEnvironment(new DataverseEnvironment("env-1", "https://ce.example", "ce-tenant"));
            session.InsertProtectedSecret(protectedSecret);
            session.UpsertServicePrincipal(new ServicePrincipal(
                "sp-1", "env-1", "client", AuthMode.ClientSecret, protectedSecret.Id, null, AuthTarget.Fo));
            session.SetSetting("RawLegacy", "keep-shape");
            session.SetDefaultEnvironment("env-1");
            throw new InvalidOperationException("late failure");
        }, CancellationToken.None));

        Assert.Empty(await store.GetEnvironmentsAsync(CancellationToken.None));
        Assert.Null(await store.GetSettingAsync("RawLegacy", CancellationToken.None));
        Assert.Null(await store.GetSettingAsync("DefaultEnvId", CancellationToken.None));
        Assert.Null(await vault.ReadSecretAsync<SecretPayload>(protectedSecret.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Late_SQL_trigger_failure_rolls_back_blob_pointer_and_profile_rows()
    {
        var (_, store, vault) = await NewStoreAsync();
        await using (var connection = new SqliteConnection(store.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER FailLateSetting BEFORE INSERT ON Settings WHEN NEW.Key='FailLate' BEGIN SELECT RAISE(ABORT,'late setting failed'); END;";
            await trigger.ExecuteNonQueryAsync(CancellationToken.None);
        }
        var protectedSecret = vault.PrepareSecret("ClientSecret", new SecretPayload("trigger-secret"));

        await Assert.ThrowsAsync<SqliteException>(() => store.RunProfileMutationAsync<int>(session =>
        {
            session.UpsertEnvironment(Environment());
            session.InsertProtectedSecret(protectedSecret);
            session.UpsertServicePrincipal(new ServicePrincipal(
                "sp", "env-1", "client", AuthMode.ClientSecret, protectedSecret.Id, null, AuthTarget.Fo));
            session.SetSetting("FailLate", "boom");
            return 0;
        }, CancellationToken.None));

        Assert.Empty(await store.GetEnvironmentsAsync(CancellationToken.None));
        Assert.Null(await vault.ReadSecretAsync<SecretPayload>(protectedSecret.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Successful_mutation_roundtrips_every_profile_row_and_existing_vault_reader()
    {
        var (_, store, vault) = await NewStoreAsync();
        var protectedSecret = vault.PrepareSecret("ClientSecret", new SecretPayload("success-secret"));
        Assert.DoesNotContain("success-secret", Encoding.Latin1.GetString(protectedSecret.Ciphertext.Span));
        Assert.Null(await vault.ReadSecretAsync<SecretPayload>(protectedSecret.Id, CancellationToken.None));

        var result = await store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment(name: "Saved"));
            session.UpsertDataverseEnvironment(new DataverseEnvironment("env-1", "https://ce.example", "ce-tenant"));
            session.InsertProtectedSecret(protectedSecret);
            session.UpsertServicePrincipal(new ServicePrincipal(
                "sp-1", "env-1", "client", AuthMode.ClientSecret, protectedSecret.Id, null, AuthTarget.Fo));
            session.SetSetting("RawLegacy", "raw-value");
            session.SetDefaultEnvironment("env-1");
            Assert.Equal("Saved", session.GetEnvironment("env-1")!.Name);
            Assert.Single(session.GetEnvironments());
            Assert.Equal("https://ce.example", session.GetDataverseEnvironment("env-1")!.BaseUrl);
            Assert.Equal(protectedSecret.Id, session.GetServicePrincipal("env-1", AuthTarget.Fo)!.SecretRef);
            Assert.Single(session.GetServicePrincipals("env-1"));
            Assert.Equal("raw-value", session.GetSetting("RawLegacy"));
            Assert.Equal("env-1", session.GetDefaultEnvironmentId());
            return "committed";
        }, CancellationToken.None);

        Assert.Equal("committed", result);
        Assert.Equal("Saved", (await store.GetEnvironmentsAsync(CancellationToken.None)).Single().Name);
        Assert.Equal("https://ce.example", (await store.GetDataverseEnvironmentAsync("env-1", CancellationToken.None))!.BaseUrl);
        Assert.Equal(protectedSecret.Id,
            (await store.GetServicePrincipalAsync("env-1", AuthTarget.Fo, CancellationToken.None))!.SecretRef);
        Assert.Equal("raw-value", await store.GetSettingAsync("RawLegacy", CancellationToken.None));
        Assert.Equal("env-1", await store.GetSettingAsync("DefaultEnvId", CancellationToken.None));
        Assert.Equal("success-secret",
            (await vault.ReadSecretAsync<SecretPayload>(protectedSecret.Id, CancellationToken.None))!.Value);
    }

    [Fact]
    public async Task Shared_secret_survives_until_no_principal_or_setting_references_it()
    {
        var (_, store, vault) = await NewStoreAsync();
        var secret = vault.PrepareSecret("ClientSecret", new SecretPayload("shared"));
        await store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment("env-1"));
            session.UpsertEnvironment(Environment("env-2"));
            session.InsertProtectedSecret(secret);
            session.UpsertServicePrincipal(new ServicePrincipal("sp1", "env-1", "c1", AuthMode.ClientSecret, secret.Id, null, AuthTarget.Fo));
            session.UpsertServicePrincipal(new ServicePrincipal("sp2", "env-2", "c2", AuthMode.ClientSecret, secret.Id, null, AuthTarget.Fo));
            session.SetSetting("LegacySecretRef", secret.Id);
            return 0;
        }, CancellationToken.None);

        await store.RunProfileMutationAsync(session =>
        {
            session.DeleteServicePrincipal("sp1");
            Assert.False(session.DeleteSecretIfUnreferenced(secret.Id));
            session.DeleteServicePrincipal("sp2");
            Assert.False(session.DeleteSecretIfUnreferenced(secret.Id));
            session.SetSetting("LegacySecretRef", null);
            Assert.True(session.DeleteSecretIfUnreferenced(secret.Id));
            return 0;
        }, CancellationToken.None);

        Assert.Null(await vault.ReadSecretAsync<SecretPayload>(secret.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Environment_delete_default_clear_and_reference_cleanup_commit_together()
    {
        var (_, store, vault) = await NewStoreAsync();
        var secret = vault.PrepareSecret("ClientSecret", new SecretPayload("delete-me"));
        await store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment());
            session.InsertProtectedSecret(secret);
            session.UpsertServicePrincipal(new ServicePrincipal(
                "sp", "env-1", "client", AuthMode.ClientSecret, secret.Id, null, AuthTarget.Fo));
            session.SetDefaultEnvironment("env-1");
            return 0;
        }, CancellationToken.None);

        await store.RunProfileMutationAsync(session =>
        {
            session.DeleteEnvironment("env-1");
            session.SetDefaultEnvironment(null);
            Assert.True(session.DeleteSecretIfUnreferenced(secret.Id));
            return 0;
        }, CancellationToken.None);

        Assert.Empty(await store.GetEnvironmentsAsync(CancellationToken.None));
        Assert.Null(await store.GetSettingAsync("DefaultEnvId", CancellationToken.None));
        Assert.Null(await vault.ReadSecretAsync<SecretPayload>(secret.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Unrelated_and_legacy_rows_survive_a_targeted_mutation()
    {
        var (_, store, _) = await NewStoreAsync();
        await store.UpsertEnvironmentAsync(Environment("legacy"), CancellationToken.None);
        await store.UpsertServicePrincipalAsync(new ServicePrincipal(
            "legacy-sp", "legacy", string.Empty, AuthMode.BearerToken, "legacy-ref", "thumb", AuthTarget.Fo),
            CancellationToken.None);
        await store.SetSettingAsync("UnknownRawMode", "future-value", CancellationToken.None);

        await store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment("new"));
            session.SetSetting("OnlyNew", "value");
            return 0;
        }, CancellationToken.None);

        var legacy = await store.GetServicePrincipalAsync("legacy", AuthTarget.Fo, CancellationToken.None);
        Assert.Equal(AuthMode.BearerToken, legacy!.AuthMode);
        Assert.Equal("legacy-ref", legacy.SecretRef);
        Assert.Equal("future-value", await store.GetSettingAsync("UnknownRawMode", CancellationToken.None));
    }

    [Fact]
    public async Task Escaped_session_refuses_operations_after_callback_completion()
    {
        var (_, store, _) = await NewStoreAsync();
        ProfileMutationSession? escaped = null;
        await store.RunProfileMutationAsync(session => { escaped = session; return 0; }, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => escaped!.GetSetting("anything"));
    }

    [Fact]
    public async Task Precancelled_mutation_never_invokes_callback()
    {
        var (_, store, _) = await NewStoreAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.RunProfileMutationAsync(session => { invoked = true; return 0; }, cancellation.Token));

        Assert.False(invoked);
    }

    [Fact]
    public async Task Task_returning_callback_is_rejected_without_invocation_or_writes()
    {
        var (_, store, _) = await NewStoreAsync();
        var invoked = false;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RunProfileMutationAsync(session =>
            {
                invoked = true;
                session.UpsertEnvironment(Environment());
                return Task.FromResult(1);
            }, CancellationToken.None));

        Assert.Contains("synchronous", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(invoked);
        Assert.Empty(await store.GetEnvironmentsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ValueTask_returning_callback_is_rejected_without_invocation()
    {
        var (_, store, _) = await NewStoreAsync();
        var invoked = false;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RunProfileMutationAsync(session =>
            {
                invoked = true;
                return ValueTask.FromResult(1);
            }, CancellationToken.None));

        Assert.Contains("synchronous", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(invoked);
    }

    [Fact]
    public async Task Session_accepts_owner_thread_and_refuses_concurrent_cross_thread_use()
    {
        var (_, store, _) = await NewStoreAsync();
        await store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment());
            Assert.Equal("Original", session.GetEnvironment("env-1")!.Name);

            Exception? crossThreadError = null;
            using var finished = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try { session.SetSetting("CrossThread", "forbidden"); }
                catch (Exception ex) { crossThreadError = ex; }
                finally { finished.Set(); }
            });
            thread.Start();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<InvalidOperationException>(crossThreadError);
            Assert.Null(session.GetSetting("CrossThread"));
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Cancellation_immediately_before_commit_rolls_back_every_change()
    {
        var (_, store, _) = await NewStoreAsync();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment());
            session.SetSetting("Marker", "changed");
            cancellation.Cancel();
            return 0;
        }, cancellation.Token));

        Assert.Empty(await store.GetEnvironmentsAsync(CancellationToken.None));
        Assert.Null(await store.GetSettingAsync("Marker", CancellationToken.None));
    }

    [Fact]
    public async Task SQLite_automatic_rollback_does_not_mask_the_original_trigger_failure()
    {
        var (_, store, _) = await NewStoreAsync();
        await using (var connection = new SqliteConnection(store.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER ForceRollback BEFORE INSERT ON Settings WHEN NEW.Key='RollbackNow' BEGIN SELECT RAISE(ROLLBACK,'original rollback marker'); END;";
            await trigger.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var error = await Assert.ThrowsAsync<SqliteException>(() => store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment());
            session.SetSetting("RollbackNow", "value");
            return 0;
        }, CancellationToken.None));

        Assert.Contains("original rollback marker", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.GetEnvironmentsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_after_successful_commit_does_not_invent_a_rollback()
    {
        var (_, store, _) = await NewStoreAsync();
        using var cancellation = new CancellationTokenSource();
        var result = await store.RunProfileMutationAsync(session =>
        {
            session.UpsertEnvironment(Environment());
            return "committed";
        }, cancellation.Token);

        cancellation.Cancel();
        Assert.Equal("committed", result);
        Assert.Single(await store.GetEnvironmentsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Gated_callback_runs_off_caller_and_outer_task_settles_after_release()
    {
        var (_, store, _) = await NewStoreAsync();
        using var release = new ManualResetEventSlim(false);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = store.RunProfileMutationAsync(session =>
        {
            entered.TrySetResult();
            release.Wait(watchdog.Token);
            session.UpsertEnvironment(Environment());
            return 1;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        try
        {
            Assert.False(operation.IsCompleted);
        }
        finally
        {
            release.Set();
        }
        Assert.Equal(1, await operation.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }
}
