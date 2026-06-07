using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises the real <see cref="CoreProfileStore"/> against a throwaway SQLite database — the same
/// store/service the WPF app uses. Cross-platform (no DPAPI on this path), so it runs on Linux CI.
/// </summary>
public sealed class CoreProfileStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"toolbax-test-{Guid.NewGuid():N}.db");

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
    public async Task Save_round_trips_the_fo_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "USMF", "", EnvStatus.Disconnected)
        {
            ClientId = "11111111-2222-3333-4444-555555555555",
            AuthMode = FoAuthMode.Certificate,
        });

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
    public async Task Legacy_bearer_token_service_principal_loads_as_interactive()
    {
        // A profile created by the WPF app with a captured/pasted bearer token (AuthMode.BearerToken)
        // and no Avalonia fo.authMode setting. BearerToken is a DELEGATED token mode — not app-only —
        // so it must surface as Interactive (MFA), not mis-mapped to the client-credentials path (which
        // rejects BearerToken with "Auth mode 'BearerToken' is not supported for the client-credentials flow").
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
        Assert.Equal(FoAuthMode.Interactive, profile.AuthMode);
        Assert.Equal(FoAuthMode.Interactive, profile.DataverseAuthMode);
        // The SP's own client id is kept as the interactive client id.
        Assert.Equal("client-from-wpf", profile.ClientId);
        Assert.Equal("dv-from-wpf", profile.DataverseClientId);
    }

    [Fact]
    public async Task Interactive_with_no_client_id_falls_back_to_the_default_global_client()
    {
        // A delegated (Interactive) environment with no configured client id (e.g. a legacy bearer-token
        // profile whose SP carried no client id) gets Microsoft's global public client so an interactive
        // sign-in has a usable client id out of the box — matching the Profiles UI default.
        var ct = TestContext.Current.CancellationToken;
        var seed = NewService();
        await seed.EnsureCreatedAsync(ct);
        await seed.UpsertEnvironmentAsync(new FoEnvironment("env1", "Ricoh Dev", "https://ricoh.dynamics.com", "tenant-1", "USMF"), ct);
        await seed.UpsertServicePrincipalAsync(
            new ServicePrincipal("env1:fo", "env1", string.Empty, AuthMode.BearerToken, null, null, AuthTarget.Fo), ct);

        var store = await CoreProfileStore.CreateAsync(NewService(), ct);

        var profile = store.GetAll().Single(p => p.Id == "env1");
        Assert.Equal(FoAuthMode.Interactive, profile.AuthMode);
        Assert.Equal(FoAuthModeExtensions.DefaultInteractiveClientId, profile.ClientId);
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
    public async Task Save_round_trips_the_dataverse_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "USMF", "", EnvStatus.Disconnected)
        {
            DataverseUrl = "https://ce.example",
            DataverseClientId = "99999999-8888-7777-6666-555555555555",
            DataverseAuthMode = FoAuthMode.Certificate,
        });

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
