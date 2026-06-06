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
    public async Task Clearing_client_id_removes_the_service_principal()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await CoreProfileStore.CreateAsync(NewService(), ct);
        store.Save(new EnvProfile("env1", "One", "https://one", "t", "", "", EnvStatus.Disconnected) { ClientId = "abc" });

        store.Save(store.GetAll().Single() with { ClientId = null });

        Assert.Null(await NewService().GetServicePrincipalAsync("env1", AuthTarget.Fo, ct));
        var reopened = await CoreProfileStore.CreateAsync(NewService(), ct);
        Assert.Null(reopened.GetAll().Single().ClientId);
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
