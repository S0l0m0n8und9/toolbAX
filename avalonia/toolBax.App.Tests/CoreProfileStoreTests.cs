using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises the real <see cref="CoreProfileStore"/> against a throwaway SQLite database — the same
/// store/service the WPF app uses. Cross-platform (no DPAPI on this path), so it runs on Linux CI.
/// </summary>
public sealed class CoreProfileStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"toolbax-test-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Connection string for every connection this fixture opens itself. Pooling is off deliberately —
    /// see the matching note in <see cref="CoreSecretStoreTests"/>: each database lives for one test
    /// method, so pooling only parks an open handle on a file <see cref="Dispose"/> is about to delete and
    /// exposes it to any other class calling the process-global <c>ClearAllPools()</c>. Test determinism
    /// only; <see cref="ProfileStore"/> builds the product's connection string itself and is untouched.
    /// </summary>
    private string ConnectionString => $"Data Source={_dbPath};Pooling=False";

    private ProfileService NewService() => new(new ProfileStore(_dbPath));

    [Fact]
    public async Task Loads_and_maps_seeded_environments()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "USMF Dev", "https://contoso.dynamics.com", "tenant-1", "USMF"), ct);
        await seed.UpsertDataverseEnvironmentAsync(new DataverseEnvironment("env1", "https://contoso.crm.dynamics.com", "tenant-1"), ct);
        await seed.SetDefaultEnvironmentAsync("env1", ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        var profile = Assert.Single(store.GetAll());
        Assert.Equal("env1", profile.Id);
        Assert.Equal("USMF Dev", profile.Name);
        Assert.Equal("https://contoso.dynamics.com", profile.Url);
        Assert.Equal("tenant-1", profile.Tenant);
        Assert.Equal("USMF", profile.Legal);
        Assert.Equal("https://contoso.crm.dynamics.com", profile.DataverseUrl);
        Assert.Equal("env1", store.ActiveId);
    }

    [Fact]
    public async Task Save_persists_a_new_profile_to_the_database()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.Save(new EnvProfile("env2", "EMEA UAT", "https://emea.dynamics.com", "tenant-2", "DEMF",
            "Tier 2", EnvStatus.Disconnected));

        // Reflected in the in-memory list...
        Assert.Contains(store.GetAll(), p => p.Id == "env2" && p.Name == "EMEA UAT");

        // ...and persisted: a fresh store over the same DB sees it.
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        var persisted = reopened.GetAll().Single(p => p.Id == "env2");
        Assert.Equal("https://emea.dynamics.com", persisted.Url);
        Assert.Equal("DEMF", persisted.Legal);
    }

    [Fact]
    public async Task Save_persists_data_integrator_and_gateway_config()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.Save(new EnvProfile("env-di", "DI Env", "https://di.dynamics.com", "tenant-di", "USMF",
            "Tier 1", EnvStatus.Disconnected)
        {
            DataIntegratorClientId = "di-client-id",
            DataIntegratorMode = DiAuthMode.Ropc,
            DualWriteGatewayUrl = "https://gw.example.powerapps.com",
        });

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        var persisted = reopened.GetAll().Single(p => p.Id == "env-di");
        Assert.Equal("di-client-id", persisted.DataIntegratorClientId);
        Assert.Equal(DiAuthMode.Ropc, persisted.DataIntegratorMode);
        Assert.Equal("https://gw.example.powerapps.com", persisted.DualWriteGatewayUrl);
    }

    [Fact]
    public async Task Clearing_data_integrator_and_gateway_config_persists_as_cleared()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env-di", "DI", "https://x", "t", "USMF", "", EnvStatus.Disconnected)
        {
            DataIntegratorClientId = "c",
            DataIntegratorMode = DiAuthMode.Ropc, // explicit non-default, so the round-trip is real
            DualWriteGatewayUrl = "https://gw",
        });

        store.Save(new EnvProfile("env-di", "DI", "https://x", "t", "USMF", "", EnvStatus.Disconnected));

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        var persisted = reopened.GetAll().Single(p => p.Id == "env-di");
        Assert.Null(persisted.DataIntegratorClientId);
        Assert.Equal(DiAuthMode.Interactive, persisted.DataIntegratorMode); // mode row removed → default
        Assert.Null(persisted.DualWriteGatewayUrl);
    }

    [Fact]
    public async Task Save_updates_an_existing_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Old", "https://old", "t", null), ct);
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.Save(new EnvProfile("env1", "Renamed", "https://new", "t", "USMF", "", EnvStatus.Disconnected));

        Assert.Single(store.GetAll());
        Assert.Equal("Renamed", store.GetAll().Single().Name);
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Equal("https://new", reopened.GetAll().Single().Url);
    }

    [Fact]
    public async Task Active_id_round_trips_through_the_store()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", null), ct);
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.ActiveId = "env1";

        Assert.Equal("env1", store.ActiveId);
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Equal("env1", reopened.ActiveId);
    }

    [Fact]
    public async Task Active_id_cache_stays_old_when_persistence_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", null), ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env2", "Two", "https://two", "t", null), ct);
        await seed.SetDefaultEnvironmentAsync("env1", ct);
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TRIGGER BlockDefaultEnvironmentUpdate
                BEFORE UPDATE OF Value ON Settings
                WHEN OLD.Key = 'DefaultEnvId'
                BEGIN
                    SELECT RAISE(ABORT, 'blocked default env update');
                END;
                """;
            await create.ExecuteNonQueryAsync(ct);
        }

        try
        {
            var error = Assert.Throws<SqliteException>(() => store.ActiveId = "env2");
            Assert.Contains("blocked default env update", error.Message);
            Assert.Equal("env1", store.ActiveId);
            Assert.Equal("env1", await NewService().GetDefaultEnvironmentIdAsync(ct));
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TRIGGER BlockDefaultEnvironmentUpdate";
            await drop.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task Clearing_dataverse_url_persists_as_no_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", null), ct);
        await seed.UpsertDataverseEnvironmentAsync(new DataverseEnvironment("env1", "https://ce.example", "t"), ct);
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Equal("https://ce.example", store.GetAll().Single().DataverseUrl);

        store.Save(store.GetAll().Single() with { DataverseUrl = null });

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Null(reopened.GetAll().Single().DataverseUrl); // stale row not resurrected
    }

    [Fact]
    public async Task Clearing_active_id_persists_as_none_active()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", null), ct);
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.ActiveId = "env1";

        store.ActiveId = null;

        Assert.Null(store.ActiveId);
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Null(reopened.ActiveId); // not silently restored from the DB
    }

    [Fact]
    public async Task Delete_removes_the_profile_and_clears_active_if_it_was_active()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", null), ct);
        await seed.SetDefaultEnvironmentAsync("env1", ct);
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.Delete("env1");

        Assert.Empty(store.GetAll());
        Assert.Null(store.ActiveId);
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Empty(reopened.GetAll());
        Assert.Null(reopened.ActiveId);
    }

    [Fact]
    public async Task Load_round_trips_a_legacy_fo_certificate_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", "USMF"), ct);
        await seed.SetSettingAsync("fo.authMode:env1", nameof(FoAuthMode.Certificate), ct);
        await seed.UpsertServicePrincipalAsync(new ServicePrincipal("env1:fo", "env1",
            "11111111-2222-3333-4444-555555555555", AuthMode.Certificate, null, "thumb", AuthTarget.Fo), ct);

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        var profile = reopened.GetAll().Single(p => p.Id == "env1");
        Assert.Equal("11111111-2222-3333-4444-555555555555", profile.ClientId);
        Assert.Equal(FoAuthMode.Certificate, profile.AuthMode);

        // Verified at the FoToolbox layer too: a Fo service principal exists.
        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct);
        Assert.NotNull(sp);
        Assert.Equal("11111111-2222-3333-4444-555555555555", sp!.ClientId);
    }

    [Fact]
    public async Task Interactive_auth_mode_round_trips_for_fo_and_dataverse()
    {
        // Interactive isn't an app-only SP mode (no FoToolbox AuthMode value), so it must round-trip via
        // the Settings k/v rather than the service principal's AuthMode.
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "USMF", "", EnvStatus.Disconnected)
        {
            ClientId = FoAuthModeExtensions.DefaultInteractiveClientId,
            AuthMode = FoAuthMode.Interactive,
            DataverseUrl = "https://ce.example",
            DataverseClientId = FoAuthModeExtensions.DefaultInteractiveClientId,
            DataverseAuthMode = FoAuthMode.Interactive,
        });

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        var profile = reopened.GetAll().Single(p => p.Id == "env1");
        Assert.Equal(FoAuthMode.Interactive, profile.AuthMode);
        Assert.Equal(FoAuthMode.Interactive, profile.DataverseAuthMode);
        // The (public) client ids round-trip via Settings…
        Assert.Equal(FoAuthModeExtensions.DefaultInteractiveClientId, profile.ClientId);
        Assert.Equal(FoAuthModeExtensions.DefaultInteractiveClientId, profile.DataverseClientId);
        // …and NO app-only service principal is created for a delegated (Interactive) mode.
        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct));
        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct));
    }

    [Fact]
    public async Task Legacy_bearer_token_service_principal_loads_as_unsupported()
    {
        // A profile created by the WPF app with a captured/pasted bearer token (AuthMode.BearerToken)
        // and no Avalonia fo.authMode setting. BearerToken is a DELEGATED token mode — not app-only —
        // so it must surface as unsupported in the App rather than silently becoming Interactive.
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Ricoh Dev", "https://ricoh.dynamics.com", "tenant-1", "USMF"), ct);
        await seed.UpsertServicePrincipalAsync(
            new ServicePrincipal("env1:fo", "env1", "client-from-wpf", AuthMode.BearerToken, null, null, AuthTarget.Fo), ct);
        await seed.UpsertServicePrincipalAsync(
            new ServicePrincipal("env1:dataverse", "env1", "dv-from-wpf", AuthMode.BearerToken, null, null, AuthTarget.Dataverse), ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        var profile = store.GetAll().Single(p => p.Id == "env1");
        Assert.Equal(FoAuthMode.Unsupported, profile.AuthMode);
        Assert.Equal(FoAuthMode.Unsupported, profile.DataverseAuthMode);
        // The SP's own client id remains visible for explicit replacement.
        Assert.Equal("client-from-wpf", profile.ClientId);
        Assert.Equal("dv-from-wpf", profile.DataverseClientId);
    }

    [Fact]
    public async Task Unrelated_save_preserves_raw_unknown_auth_DI_settings_principals_and_blobs_across_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Legacy", "https://legacy", "tenant", "USMF"), ct);
        await seed.SetSettingAsync("fo.authMode:env1", "FutureAuth", ct);
        await seed.SetSettingAsync("dv.authMode:env1", "99", ct);
        await seed.SetSettingAsync("di.mode:env1", "FutureDi", ct);
        await seed.SetSettingAsync("dw.gatewayUrl:env1", "https://legacy-gateway", ct);
        await seed.SetSettingAsync(CoreSecretStore.DiSecretRefSettingKey("env1"), "di-row", ct);
        await seed.UpsertServicePrincipalAsync(new ServicePrincipal(
            "legacy-fo", "env1", "fo-client", AuthMode.BearerToken, "fo-row", "FO-CERT", AuthTarget.Fo), ct);
        await seed.UpsertServicePrincipalAsync(new ServicePrincipal(
            "legacy-dv", "env1", "dv-client", AuthMode.Certificate, "dv-row", "DV-CERT", AuthTarget.Dataverse), ct);
        InsertVaultRow("fo-row");
        InsertVaultRow("dv-row");
        InsertVaultRow("di-row");

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        var loaded = Assert.Single(store.GetAll());
        Assert.Equal(FoAuthMode.Unsupported, loaded.AuthMode);
        Assert.Equal(FoAuthMode.Unsupported, loaded.DataverseAuthMode);
        Assert.Equal(DiAuthMode.Unsupported, loaded.DataIntegratorMode);

        store.Save(loaded with { Name = "Renamed" });

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Equal("Renamed", Assert.Single(reopened.GetAll()).Name);
        Assert.Equal("FutureAuth", await NewService().GetSettingAsync("fo.authMode:env1", ct));
        Assert.Equal("99", await NewService().GetSettingAsync("dv.authMode:env1", ct));
        Assert.Equal("FutureDi", await NewService().GetSettingAsync("di.mode:env1", ct));
        Assert.Equal("https://legacy-gateway", await NewService().GetSettingAsync("dw.gatewayUrl:env1", ct));
        Assert.Equal("di-row", await NewService().GetSettingAsync(CoreSecretStore.DiSecretRefSettingKey("env1"), ct));
        var fo = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct);
        Assert.Equal(("legacy-fo", AuthMode.BearerToken, "fo-row", "FO-CERT"),
            (fo!.Id, fo.AuthMode, fo.SecretRef, fo.CertThumbprint));
        var dv = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct);
        Assert.Equal(("legacy-dv", AuthMode.Certificate, "dv-row", "DV-CERT"),
            (dv!.Id, dv.AuthMode, dv.SecretRef, dv.CertThumbprint));
        Assert.Equal(1, CountVaultRows("fo-row"));
        Assert.Equal(1, CountVaultRows("dv-row"));
        Assert.Equal(1, CountVaultRows("di-row"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Present_blank_mode_settings_are_unsupported_and_preserved_on_unrelated_save(string rawMode)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Legacy", "https://legacy", "tenant", "USMF"), ct);
        await seed.SetSettingAsync("fo.authMode:env1", rawMode, ct);
        await seed.SetSettingAsync("dv.authMode:env1", rawMode, ct);
        await seed.SetSettingAsync("di.mode:env1", rawMode, ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        var loaded = Assert.Single(store.GetAll());
        Assert.Equal(FoAuthMode.Unsupported, loaded.AuthMode);
        Assert.Equal(FoAuthMode.Unsupported, loaded.DataverseAuthMode);
        Assert.Equal(DiAuthMode.Unsupported, loaded.DataIntegratorMode);

        store.Save(loaded with { Name = "Renamed" });

        Assert.Equal(rawMode, await NewService().GetSettingAsync("fo.authMode:env1", ct));
        Assert.Equal(rawMode, await NewService().GetSettingAsync("dv.authMode:env1", ct));
        Assert.Equal(rawMode, await NewService().GetSettingAsync("di.mode:env1", ct));
    }

    [Theory]
    [InlineData(AuthTarget.Fo)]
    [InlineData(AuthTarget.Dataverse)]
    public async Task Same_client_legacy_mode_replacement_unbinds_incompatible_credentials_after_upsert(AuthTarget target)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = target == AuthTarget.Fo
            ? await SeedFoSecretAsync("same-client", "old-row")
            : await SeedDataverseSecretAsync("same-client", "old-row");
        var service = NewService();
        var existing = await service.GetServicePrincipalAsync("env1", target, ct);
        await service.UpsertServicePrincipalAsync(existing! with
        {
            AuthMode = AuthMode.Certificate,
            CertThumbprint = "OLD-CERT",
        }, ct);
        await service.SetSettingAsync(
            target == AuthTarget.Fo ? "fo.authMode:env1" : "dv.authMode:env1",
            nameof(FoAuthMode.Certificate), ct);
        store = await CoreProfileStore.CreateAsync(NewService(), ct);
        var profile = Assert.Single(store.GetAll());
        Assert.Equal(FoAuthMode.Certificate,
            target == AuthTarget.Fo ? profile.AuthMode : profile.DataverseAuthMode);

        store.Save(target == AuthTarget.Fo
            ? profile with { AuthMode = FoAuthMode.ClientSecret }
            : profile with { DataverseAuthMode = FoAuthMode.ClientSecret });

        var replaced = await NewService().GetServicePrincipalAsync("env1", target, ct);
        Assert.Equal(AuthMode.ClientSecret, replaced!.AuthMode);
        Assert.Null(replaced.SecretRef);
        Assert.Null(replaced.CertThumbprint);
        Assert.Equal(0, CountVaultRows("old-row"));
    }

    [Theory]
    [InlineData(AuthTarget.Fo)]
    [InlineData(AuthTarget.Dataverse)]
    public async Task Failed_same_client_legacy_replacement_preserves_old_row_and_blob(AuthTarget target)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = target == AuthTarget.Fo
            ? await SeedFoSecretAsync("same-client", "old-row")
            : await SeedDataverseSecretAsync("same-client", "old-row");
        var service = NewService();
        var existing = await service.GetServicePrincipalAsync("env1", target, ct);
        await service.UpsertServicePrincipalAsync(existing! with
        {
            AuthMode = AuthMode.Certificate,
            CertThumbprint = "OLD-CERT",
        }, ct);
        await service.SetSettingAsync(
            target == AuthTarget.Fo ? "fo.authMode:env1" : "dv.authMode:env1",
            nameof(FoAuthMode.Certificate), ct);
        store = await CoreProfileStore.CreateAsync(NewService(), ct);
        var profile = Assert.Single(store.GetAll());
        RejectServicePrincipalUpdates();

        Assert.Throws<SqliteException>(() => store.Save(target == AuthTarget.Fo
            ? profile with { AuthMode = FoAuthMode.ClientSecret }
            : profile with { DataverseAuthMode = FoAuthMode.ClientSecret }));

        var preserved = await NewService().GetServicePrincipalAsync("env1", target, ct);
        Assert.Equal(AuthMode.Certificate, preserved!.AuthMode);
        Assert.Equal("old-row", preserved.SecretRef);
        Assert.Equal("OLD-CERT", preserved.CertThumbprint);
        Assert.Equal(1, CountVaultRows("old-row"));
    }

    [Theory]
    [InlineData(AuthTarget.Fo)]
    [InlineData(AuthTarget.Dataverse)]
    public async Task Real_vault_secret_is_blocked_until_legacy_replacement_is_saved_then_survives_rename(AuthTarget target)
    {
        if (!OperatingSystem.IsWindows()) return;
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Legacy", "https://legacy", "tenant", "USMF"), ct);
        await seed.UpsertDataverseEnvironmentAsync(new DataverseEnvironment("env1", "https://legacy.crm.dynamics.com", "tenant"), ct);
        var clientId = target == AuthTarget.Fo ? "fo-client" : "dv-client";
        await seed.SetSettingAsync(target == AuthTarget.Fo ? "fo.authMode:env1" : "dv.authMode:env1",
            nameof(FoAuthMode.Certificate), ct);
        await seed.UpsertServicePrincipalAsync(new ServicePrincipal(
            target == AuthTarget.Fo ? "legacy-fo" : "legacy-dv", "env1", clientId,
            AuthMode.Certificate, null, "OLD-CERT", target), ct);
        var profileStore = await CoreProfileStore.CreateAsync(NewService(), ct);
        var secretStore = new CoreSecretStore(NewService(), new SecretVaultService(ConnectionString));
        var secretTarget = target == AuthTarget.Fo ? SecretTarget.Fo : SecretTarget.Dataverse;
        secretStore.SetSecret("env1", "legacy-secret", secretTarget);
        var oldRef = (await NewService().GetServicePrincipalAsync("env1", target, ct))!.SecretRef!;
        var vm = new ProfilesViewModel(profileStore, secretStore);
        if (target == AuthTarget.Fo)
        {
            vm.SelectedFoAuthMode = FoAuthMode.ClientSecret;
            vm.SecretInput = "new-secret";
            vm.SaveSecretCommand.Execute(null);
        }
        else
        {
            vm.SelectedDataverseAuthMode = FoAuthMode.ClientSecret;
            vm.DataverseSecretInput = "new-secret";
            vm.SaveDataverseSecretCommand.Execute(null);
        }

        Assert.Equal(oldRef, (await NewService().GetServicePrincipalAsync("env1", target, ct))!.SecretRef);
        Assert.Equal(1, CountVaultRows(oldRef));

        if (target == AuthTarget.Fo) vm.ClearSecretCommand.Execute(null);
        else vm.ClearDataverseSecretCommand.Execute(null);

        Assert.Equal(oldRef, (await NewService().GetServicePrincipalAsync("env1", target, ct))!.SecretRef);
        Assert.Equal(1, CountVaultRows(oldRef));

        await vm.SaveCommand.ExecuteAsync(null);
        if (target == AuthTarget.Fo)
        {
            vm.SecretInput = "new-secret";
            vm.SaveSecretCommand.Execute(null);
        }
        else
        {
            vm.DataverseSecretInput = "new-secret";
            vm.SaveDataverseSecretCommand.Execute(null);
        }
        var newRef = (await NewService().GetServicePrincipalAsync("env1", target, ct))!.SecretRef!;
        Assert.NotEqual(oldRef, newRef);
        Assert.Equal(0, CountVaultRows(oldRef));
        Assert.Equal(1, CountVaultRows(newRef));

        vm.DraftName = "Renamed";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(newRef, (await NewService().GetServicePrincipalAsync("env1", target, ct))!.SecretRef);
        Assert.Equal(1, CountVaultRows(newRef));
    }

    [Fact]
    public async Task Changing_client_while_legacy_mode_remains_unsupported_is_rejected_before_environment_write()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Legacy", "https://legacy", "tenant", "USMF"), ct);
        await seed.SetSettingAsync("fo.authMode:env1", "FutureAuth", ct);
        await seed.UpsertServicePrincipalAsync(new ServicePrincipal(
            "legacy-fo", "env1", "old-client", AuthMode.BearerToken, "old-row", null, AuthTarget.Fo), ct);
        InsertVaultRow("old-row");
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        var profile = Assert.Single(store.GetAll());

        var error = Assert.Throws<InvalidOperationException>(() =>
            store.Save(profile with { Name = "Must not persist", ClientId = "new-client" }));

        Assert.Contains("supported mode", error.Message, StringComparison.OrdinalIgnoreCase);
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Equal("Legacy", Assert.Single(reopened.GetAll()).Name);
        Assert.Equal("old-client", (await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct))!.ClientId);
        Assert.Equal(1, CountVaultRows("old-row"));
    }

    [Fact]
    public async Task Core_interactive_service_principal_loads_as_interactive()
    {
        // A profile created by the WPF app with AuthMode.Interactive (the broker's delegated mode) and
        // no Avalonia fo.authMode setting must surface as FoAuthMode.Interactive — not fall through to
        // the client-credentials default. This guards the FromCoreAuthMode explicit Interactive arm.
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Dev", "https://dev.dynamics.com", "tenant-1", "USMF"), ct);
        await seed.UpsertServicePrincipalAsync(
            new ServicePrincipal("env1:fo", "env1", "client-from-wpf", AuthMode.Interactive, null, null, AuthTarget.Fo), ct);
        await seed.UpsertServicePrincipalAsync(
            new ServicePrincipal("env1:dataverse", "env1", "dv-from-wpf", AuthMode.Interactive, null, null, AuthTarget.Dataverse), ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        var profile = store.GetAll().Single(p => p.Id == "env1");
        Assert.Equal(FoAuthMode.Interactive, profile.AuthMode);
        Assert.Equal(FoAuthMode.Interactive, profile.DataverseAuthMode);
        // The SP's client id is preserved as the interactive client id.
        Assert.Equal("client-from-wpf", profile.ClientId);
        Assert.Equal("dv-from-wpf", profile.DataverseClientId);
    }

    [Fact]
    public async Task Unknown_core_service_principal_mode_loads_as_unsupported()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Legacy", "https://legacy", "tenant", "USMF"), ct);
        await seed.UpsertServicePrincipalAsync(new ServicePrincipal(
            "legacy", "env1", "client", (AuthMode)99, "secret-ref", "thumb", AuthTarget.Fo), ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        Assert.Equal(FoAuthMode.Unsupported, Assert.Single(store.GetAll()).AuthMode);
    }

    [Fact]
    public async Task Bearer_token_with_no_client_id_remains_unsupported_without_inventing_a_client()
    {
        // A legacy bearer-token SP with no client id remains visibly unsupported. Inventing the global
        // interactive client here would silently present a different authentication mode as configured.
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Ricoh Dev", "https://ricoh.dynamics.com", "tenant-1", "USMF"), ct);
        await seed.UpsertServicePrincipalAsync(
            new ServicePrincipal("env1:fo", "env1", string.Empty, AuthMode.BearerToken, null, null, AuthTarget.Fo), ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        var profile = store.GetAll().Single(p => p.Id == "env1");
        Assert.Equal(FoAuthMode.Unsupported, profile.AuthMode);
        Assert.Null(profile.ClientId);
    }

    [Fact]
    public async Task App_only_mode_with_empty_client_id_normalises_to_null()
    {
        // An app-only SP carrying an empty client id must read back as null (not ""), so a blank
        // client id is consistently absent regardless of how it was stored.
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", null), ct);
        await seed.SetSettingAsync(FoAuthModeKeyForTest("env1"), nameof(FoAuthMode.ClientSecret), ct);
        await seed.UpsertServicePrincipalAsync(
            new ServicePrincipal("env1:fo", "env1", string.Empty, AuthMode.ClientSecret, "secret-ref", null, AuthTarget.Fo), ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        var profile = store.GetAll().Single(p => p.Id == "env1");
        Assert.Equal(FoAuthMode.ClientSecret, profile.AuthMode);
        Assert.Null(profile.ClientId);
    }

    // Mirrors CoreProfileStore.FoAuthModeKey (private) for seeding a legacy app-only auth-mode setting.
    private static string FoAuthModeKeyForTest(string envId) => $"fo.authMode:{envId}";

    [Fact]
    public async Task Clearing_client_id_removes_the_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        // An app-only mode creates the SP under test.
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected)
        {
            ClientId = "abc",
            AuthMode = FoAuthMode.ClientSecret,
        });

        store.Save(store.GetAll().Single() with { ClientId = null });

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct));
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Null(reopened.GetAll().Single().ClientId);
    }

    [Fact]
    public async Task Delete_also_removes_the_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        // An app-only mode creates the SP whose cleanup-on-delete this verifies.
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected)
        {
            ClientId = "abc",
            AuthMode = FoAuthMode.ClientSecret,
        });
        Assert.NotNull(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct));

        store.Delete("env1");

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct)); // no orphan
    }

    [Fact]
    public async Task Load_round_trips_a_legacy_dataverse_certificate_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", "USMF"), ct);
        await seed.UpsertDataverseEnvironmentAsync(new DataverseEnvironment("env1", "https://ce.example", "t"), ct);
        await seed.SetSettingAsync("dv.authMode:env1", nameof(FoAuthMode.Certificate), ct);
        await seed.UpsertServicePrincipalAsync(new ServicePrincipal("env1:dataverse", "env1",
            "99999999-8888-7777-6666-555555555555", AuthMode.Certificate, null, "thumb", AuthTarget.Dataverse), ct);

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        var profile = reopened.GetAll().Single(p => p.Id == "env1");
        Assert.Equal("99999999-8888-7777-6666-555555555555", profile.DataverseClientId);
        Assert.Equal(FoAuthMode.Certificate, profile.DataverseAuthMode);

        // Verified at the FoToolbox layer: a Target=Dataverse service principal exists, distinct from F&O.
        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct);
        Assert.NotNull(sp);
        Assert.Equal("99999999-8888-7777-6666-555555555555", sp!.ClientId);
        Assert.Equal(AuthTarget.Dataverse, sp.Target);
    }

    [Fact]
    public async Task Clearing_dataverse_client_id_removes_the_dataverse_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        // An app-only mode creates the Dataverse SP whose removal this verifies (a delegated/Interactive
        // mode never creates one, and its blank client id resolves to the global default — see
        // Interactive_with_no_client_id_falls_back_to_the_default_global_client).
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected)
        {
            DataverseClientId = "abc",
            DataverseAuthMode = FoAuthMode.ClientSecret,
        });

        store.Save(store.GetAll().Single() with { DataverseClientId = null });

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct));
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Null(reopened.GetAll().Single().DataverseClientId);
    }

    [Fact]
    public async Task Fo_and_dataverse_service_principals_coexist()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected)
        {
            ClientId = "fo-client",
            DataverseClientId = "dv-client",
        });

        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        var profile = reopened.GetAll().Single();
        Assert.Equal("fo-client", profile.ClientId);
        Assert.Equal("dv-client", profile.DataverseClientId);
    }

    // ── Secret-blob lifecycle (#165) ─────────────────────────────────────────────────────────────────
    // A stored client secret is a SecretVault row pointed at by ServicePrincipal.SecretRef. The vault has
    // no FK cascade to ServicePrincipals, so every Save path that drops or re-points an SP has to take
    // the blob with it — otherwise it's unreachable forever (HasSecret false, ClearSecret early-returns).
    // The blobs here are plain (non-DPAPI) rows, so these run on Linux CI.

    private void InsertVaultRow(string id)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO SecretVault(Id, Kind, Blob) VALUES ($id, 'test', $blob)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.Add("$blob", Microsoft.Data.Sqlite.SqliteType.Blob).Value = new byte[] { 1, 2, 3 };
        cmd.ExecuteNonQuery();
    }

    private int CountVaultRows(string id)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SecretVault WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Attaches a stored secret to an existing service principal the way CoreSecretStore would: a vault
    // blob plus the SecretRef pointing at it (CoreProfileStore.Save has no way to set a secret itself).
    private async Task AttachSecretAsync(string envId, AuthTarget target, string secretRef)
    {
        var ct = TestContext.Current.CancellationToken;
        var svc = NewService();
        var sp = await svc.GetServicePrincipalAsync(envId, target, ct);
        Assert.NotNull(sp);
        await svc.UpsertServicePrincipalAsync(sp! with { SecretRef = secretRef }, ct);
        InsertVaultRow(secretRef);
    }

    // Attaches a certificate thumbprint to an existing service principal. Unlike a secret this has no
    // vault blob to seed — the thumbprint points at the machine certificate store, so only the SP row's
    // pointer exists to be carried over (or unbound).
    private async Task AttachCertThumbprintAsync(string envId, AuthTarget target, string thumbprint)
    {
        var ct = TestContext.Current.CancellationToken;
        var svc = NewService();
        var sp = await svc.GetServicePrincipalAsync(envId, target, ct);
        Assert.NotNull(sp);
        await svc.UpsertServicePrincipalAsync(sp! with { CertThumbprint = thumbprint }, ct);
    }

    // An app-only (ClientSecret) F&O profile with a stored secret, returned as a freshly loaded store.
    private async Task<CoreProfileStore> SeedFoSecretAsync(string clientId, string secretRef)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected)
        {
            ClientId = clientId,
            AuthMode = FoAuthMode.ClientSecret,
        });
        await AttachSecretAsync("env1", AuthTarget.Fo, secretRef);
        return await CoreProfileStore.CreateAsync(NewService(), ct);
    }

    // The Dataverse mirror of SeedFoSecretAsync.
    private async Task<CoreProfileStore> SeedDataverseSecretAsync(string clientId, string secretRef)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected)
        {
            DataverseUrl = "https://ce.example",
            DataverseClientId = clientId,
            DataverseAuthMode = FoAuthMode.ClientSecret,
        });
        await AttachSecretAsync("env1", AuthTarget.Dataverse, secretRef);
        return await CoreProfileStore.CreateAsync(NewService(), ct);
    }

    [Fact]
    public async Task Switching_fo_auth_to_interactive_deletes_the_abandoned_secret_blob()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedFoSecretAsync("app-a", "fo-row-1");

        // Interactive is delegated, so the app-only SP is dropped — its secret has to go with it.
        store.Save(store.GetAll().Single() with { AuthMode = FoAuthMode.Interactive });

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct));
        Assert.Equal(0, CountVaultRows("fo-row-1"));
    }

    [Fact]
    public async Task Switching_dataverse_auth_to_interactive_deletes_the_abandoned_secret_blob()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedDataverseSecretAsync("dv-a", "dv-row-1");

        store.Save(store.GetAll().Single() with { DataverseAuthMode = FoAuthMode.Interactive });

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct));
        Assert.Equal(0, CountVaultRows("dv-row-1"));
    }

    [Fact]
    public async Task Clearing_the_fo_client_id_deletes_the_abandoned_secret_blob()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedFoSecretAsync("app-a", "fo-row-1");

        store.Save(store.GetAll().Single() with { ClientId = null });

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct));
        Assert.Equal(0, CountVaultRows("fo-row-1"));
    }

    [Fact]
    public async Task Clearing_the_dataverse_client_id_deletes_the_abandoned_secret_blob()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedDataverseSecretAsync("dv-a", "dv-row-1");

        store.Save(store.GetAll().Single() with { DataverseClientId = null });

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct));
        Assert.Equal(0, CountVaultRows("dv-row-1"));
    }

    [Fact]
    public async Task Changing_the_fo_client_id_unbinds_and_deletes_the_previous_secret()
    {
        // A secret issued for app registration A is not valid for B. Carrying it over silently re-binds
        // A's secret to B and fails at AAD as invalid_client — an error that reads like anything but
        // "the secret is for the wrong app". Unbinding makes the UI ask for B's secret instead.
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedFoSecretAsync("app-a", "fo-row-1");

        store.Save(store.GetAll().Single() with { ClientId = "app-b" });

        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct);
        Assert.NotNull(sp);
        Assert.Equal("app-b", sp!.ClientId);
        Assert.Null(sp.SecretRef);
        Assert.Equal(0, CountVaultRows("fo-row-1"));
    }

    [Fact]
    public async Task Changing_the_dataverse_client_id_unbinds_and_deletes_the_previous_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedDataverseSecretAsync("dv-a", "dv-row-1");

        store.Save(store.GetAll().Single() with { DataverseClientId = "dv-b" });

        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct);
        Assert.NotNull(sp);
        Assert.Equal("dv-b", sp!.ClientId);
        Assert.Null(sp.SecretRef);
        Assert.Equal(0, CountVaultRows("dv-row-1"));
    }

    // Makes the next service-principal upsert fail at the database, standing in for any write that can't
    // land (a locked file, a disk error, a constraint). The store's own code path is untouched — only the
    // write it attempts is rejected — so this exercises the real failure ordering.
    private void RejectServicePrincipalUpdates()
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TRIGGER RejectSpUpdate BEFORE UPDATE ON ServicePrincipals
BEGIN
  SELECT RAISE(ABORT, 'simulated write failure');
END;";
        cmd.ExecuteNonQuery();
    }

    // The delete-side counterpart of RejectServicePrincipalUpdates, for the drop-the-whole-row path.
    private void RejectServicePrincipalDeletes()
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TRIGGER RejectSpDelete BEFORE DELETE ON ServicePrincipals
BEGIN
  SELECT RAISE(ABORT, 'simulated write failure');
END;";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task A_failed_service_principal_delete_leaves_its_secret_blob_intact()
    {
        // Dropping the whole row obeys the same ordering invariant: if the row delete can't land, the
        // service principal survives — and a blob deleted ahead of it would leave that survivor's
        // SecretRef pointing at nothing. (The reverse failure is benign: an orphaned blob is collectable
        // and traced, whereas a dangling SecretRef is not.)
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedFoSecretAsync("app-a", "fo-row-1");
        RejectServicePrincipalDeletes();

        // Interactive is delegated, so this is the path that drops the app-only SP entirely.
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(
            () => store.Save(store.GetAll().Single() with { AuthMode = FoAuthMode.Interactive }));

        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct);
        Assert.NotNull(sp);                          // the row survived the failed delete…
        Assert.Equal("fo-row-1", sp!.SecretRef);
        Assert.Equal(1, CountVaultRows("fo-row-1")); // …so its secret is still there to be read
    }

    [Fact]
    public async Task A_failed_client_id_change_leaves_the_fo_secret_and_its_blob_intact()
    {
        // The blob must not be deleted before the row that points at it has been re-written: a failed
        // upsert would otherwise leave the surviving service principal's SecretRef aimed at a blob that
        // is already gone — HasSecret reads true while the secret is unreadable, and ClearSecret can't
        // reach it. Failing whole is the only consistent outcome, so the save can just be retried.
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedFoSecretAsync("app-a", "fo-row-1");
        RejectServicePrincipalUpdates();

        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(
            () => store.Save(store.GetAll().Single() with { ClientId = "app-b" }));

        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct);
        Assert.Equal("app-a", sp!.ClientId);     // the row is untouched…
        Assert.Equal("fo-row-1", sp.SecretRef);
        Assert.Equal(1, CountVaultRows("fo-row-1")); // …and still points at a blob that exists
    }

    [Fact]
    public async Task A_failed_dataverse_client_id_change_leaves_the_secret_and_its_blob_intact()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SeedDataverseSecretAsync("dv-a", "dv-row-1");
        RejectServicePrincipalUpdates();

        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(
            () => store.Save(store.GetAll().Single() with { DataverseClientId = "dv-b" }));

        var sp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct);
        Assert.Equal("dv-a", sp!.ClientId);
        Assert.Equal("dv-row-1", sp.SecretRef);
        Assert.Equal(1, CountVaultRows("dv-row-1"));
    }

    [Fact]
    public async Task Saving_an_unchanged_client_id_keeps_the_stored_credentials()
    {
        // The regression guard for the four unbind tests above: an ordinary edit (a rename) must leave
        // every credential — secret ref, vault blob and certificate thumbprint — exactly where it was.
        // All are seeded by one Save, since each Save rewrites the F&O *and* Dataverse credential from
        // the profile it's given.
        var ct = TestContext.Current.CancellationToken;
        var seeded = await CoreProfileStore.CreateAsync(NewService(), ct);
        seeded.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected)
        {
            ClientId = "app-a",
            AuthMode = FoAuthMode.ClientSecret,
            DataverseUrl = "https://ce.example",
            DataverseClientId = "dv-a",
            DataverseAuthMode = FoAuthMode.ClientSecret,
        });
        await AttachSecretAsync("env1", AuthTarget.Fo, "fo-row-1");
        await AttachSecretAsync("env1", AuthTarget.Dataverse, "dv-row-1");
        await AttachCertThumbprintAsync("env1", AuthTarget.Fo, "AA11BB22CC33");
        await AttachCertThumbprintAsync("env1", AuthTarget.Dataverse, "DD44EE55FF66");
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.Save(store.GetAll().Single() with { Name = "Renamed" });

        var foSp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct);
        Assert.Equal("fo-row-1", foSp!.SecretRef);
        Assert.Equal("AA11BB22CC33", foSp.CertThumbprint);
        Assert.Equal(1, CountVaultRows("fo-row-1"));
        var dvSp = await NewService().GetServicePrincipalAsync("env1", AuthTarget.Dataverse, ct);
        Assert.Equal("dv-row-1", dvSp!.SecretRef);
        Assert.Equal("DD44EE55FF66", dvSp.CertThumbprint);
        Assert.Equal(1, CountVaultRows("dv-row-1"));
    }

    [Fact]
    public async Task Delete_also_removes_the_data_integrator_secret()
    {
        // The DI service-account secret is a vault blob referenced from Settings (there's no SP row to
        // hang it off), so Delete has to clean up both the pointer and the blob.
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "One", "https://one", "t", null), ct);
        await seed.SetSettingAsync(CoreSecretStore.DiSecretRefSettingKey("env1"), "di-row-1", ct);
        InsertVaultRow("di-row-1");
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.Delete("env1");

        Assert.Null(await NewService().GetSettingAsync(CoreSecretStore.DiSecretRefSettingKey("env1"), ct));
        Assert.Equal(0, CountVaultRows("di-row-1"));
    }

    [Fact]
    public async Task Delete_also_removes_the_service_principal_secret_blob()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedFoSecretAsync("app-a", "fo-row-1");
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        store.Delete("env1");

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct));
        Assert.Equal(0, CountVaultRows("fo-row-1"));
    }

    [Fact]
    public async Task Empty_database_yields_no_profiles()
    {
        var store = await CoreProfileStore.CreateAsync(NewService(), TestContext.Current.CancellationToken);

        Assert.Empty(store.GetAll());
        Assert.Null(store.ActiveId);
    }

    public void Dispose()
    {
        try
        {
            // ProfileStore builds its own (pooled) connection string, so its handles can still be parked
            // in the process-global pool when we get here.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort temp cleanup: a throw from Dispose is reported as a test failure, so a lingering
            // handle on the temp file must never turn a green test red.
        }
    }
}
