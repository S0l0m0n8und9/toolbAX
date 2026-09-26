using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

// The production service is Windows-annotated. These tests use its delegate-only constructor, which
// creates no platform dependencies; all lookup and token results below are controlled in-process.
[SupportedOSPlatform("windows")]
public sealed class CoreAuthServiceSnapshotTests
{
    private static EnvProfile Profile() => new("env", "A", "https://contoso.operations.dynamics.com",
        "tenant-a", "USMF", "Tier 1", EnvStatus.Connected,
        DataverseUrl: "https://contoso.crm.dynamics.com", ClientId: "client-a", AuthMode: FoAuthMode.ClientSecret,
        DataverseClientId: "dv-client-a", DataverseAuthMode: FoAuthMode.ClientSecret);

    private static ServicePrincipal Principal(AuthTarget target) =>
        new("sp", "env", target == AuthTarget.Fo ? "client-a" : "dv-client-a", AuthMode.ClientSecret, "secret-ref", null, target);

    [Theory]
    [InlineData(AuthTarget.Fo, (int)FoAuthMode.Certificate)]
    [InlineData(AuthTarget.Fo, -1)]
    [InlineData(AuthTarget.Fo, 99)]
    [InlineData(AuthTarget.Dataverse, (int)FoAuthMode.Certificate)]
    [InlineData(AuthTarget.Dataverse, -1)]
    [InlineData(AuthTarget.Dataverse, 99)]
    public async Task Unsupported_app_mode_is_rejected_before_principal_lookup_or_token_acquisition(
        AuthTarget target, int rawMode)
    {
        var lookupCalls = 0;
        var brokerCalls = 0;
        var auth = new CoreAuthService((_, _, _) =>
        {
            lookupCalls++;
            return Task.FromResult<ServicePrincipal?>(Principal(target));
        }, (_, _) =>
        {
            brokerCalls++;
            return Task.FromResult("wrong-token");
        });
        var mode = (FoAuthMode)rawMode;
        var profile = target == AuthTarget.Fo
            ? Profile() with { AuthMode = mode }
            : Profile() with { DataverseAuthMode = mode };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => target == AuthTarget.Fo
            ? auth.AcquireFoTokenAsync(profile, TestContext.Current.CancellationToken)
            : auth.AcquireDataverseTokenAsync(profile, TestContext.Current.CancellationToken));

        Assert.Contains("unsupported", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, lookupCalls);
        Assert.Equal(0, brokerCalls);
    }

    [Theory]
    [InlineData(AuthTarget.Fo, "environment")]
    [InlineData(AuthTarget.Fo, "target")]
    [InlineData(AuthTarget.Fo, "client")]
    [InlineData(AuthTarget.Fo, "mode")]
    [InlineData(AuthTarget.Fo, "missing")]
    [InlineData(AuthTarget.Dataverse, "environment")]
    [InlineData(AuthTarget.Dataverse, "target")]
    [InlineData(AuthTarget.Dataverse, "client")]
    [InlineData(AuthTarget.Dataverse, "mode")]
    [InlineData(AuthTarget.Dataverse, "missing")]
    public async Task App_only_principal_must_match_the_captured_profile_before_broker(AuthTarget target, string mismatch)
    {
        var principal = Principal(target);
        ServicePrincipal? found = mismatch switch
        {
            "environment" => principal with { EnvId = "ENV" },
            "target" => principal with { Target = target == AuthTarget.Fo ? AuthTarget.Dataverse : AuthTarget.Fo },
            "client" => principal with { ClientId = "client-b" },
            "mode" => principal with { AuthMode = AuthMode.Certificate },
            _ => null,
        };
        var brokerCalls = 0;
        var auth = new CoreAuthService((id, requested, _) =>
        {
            Assert.Equal("env", id);
            Assert.Equal(target, requested);
            return Task.FromResult(found);
        }, (_, _) => { brokerCalls++; return Task.FromResult("wrong-token"); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => target == AuthTarget.Fo
            ? auth.AcquireFoTokenAsync(Profile(), TestContext.Current.CancellationToken)
            : auth.AcquireDataverseTokenAsync(Profile(), TestContext.Current.CancellationToken));
        Assert.Equal(0, brokerCalls);
    }

    [Theory]
    [InlineData(AuthTarget.Fo)]
    [InlineData(AuthTarget.Dataverse)]
    public async Task Matching_normalized_client_identity_keeps_the_captured_auth_request(AuthTarget target)
    {
        var profile = Profile();
        var principal = Principal(target);
        AuthTokenRequest? captured = null;
        var auth = new CoreAuthService((_, _, _) => Task.FromResult<ServicePrincipal?>(principal with
        {
            ClientId = "  " + principal.ClientId.ToUpperInvariant() + "  ",
        }), (request, _) => { captured = request; return Task.FromResult("correct-token"); });

        var token = target == AuthTarget.Fo
            ? await auth.AcquireFoTokenAsync(profile, forceRefresh: true, TestContext.Current.CancellationToken)
            : await auth.AcquireDataverseTokenAsync(profile, forceRefresh: true, TestContext.Current.CancellationToken);

        Assert.Equal("correct-token", token);
        Assert.Equal("env", captured!.Principal.EnvId);
        Assert.Equal(target, captured.Principal.Target);
        Assert.Equal(profile.Tenant, captured.TenantId);
        Assert.Equal(target == AuthTarget.Fo ? profile.Url : profile.DataverseUrl, captured.ResourceBaseUrl);
        Assert.True(captured.ForceRefresh);
    }

    [Fact]
    public async Task Saved_interactive_identity_drives_the_next_core_auth_request()
    {
        var original = Profile();
        var store = new FakeProfileStore(new[] { original }) { ActiveId = original.Id };
        using var profiles = new ProfilesViewModel(store);
        profiles.DraftAuthMode = FoAuthMode.Interactive;
        profiles.DraftClientId = "new-interactive-client";
        profiles.DraftTenant = "new-interactive-tenant";
        profiles.DraftUrl = "https://new.operations.dynamics.com/data";

        await profiles.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(store.GetAll());
        AuthTokenRequest? captured = null;
        var auth = new CoreAuthService(
            (_, _, _) => throw new InvalidOperationException("Interactive acquisition must not load an app principal."),
            (request, _) =>
            {
                captured = request;
                return Task.FromResult("saved-identity-token");
            });

        var token = await auth.AcquireFoTokenAsync(
            saved, forceRefresh: true, TestContext.Current.CancellationToken);

        Assert.Equal("saved-identity-token", token);
        Assert.Equal(FoAuthMode.Interactive, saved.AuthMode);
        Assert.Equal("new-interactive-client", captured!.Principal.ClientId);
        Assert.Equal("new-interactive-tenant", captured.TenantId);
        Assert.Equal("https://new.operations.dynamics.com", captured.ResourceBaseUrl);
        Assert.True(captured.ForceRefresh);
    }

    [Fact]
    public async Task Intermediate_B_principal_during_A_B_A_never_dispatches_or_publishes_metadata()
    {
        var active = Profile();
        var snapshot = active;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var found = new TaskCompletionSource<ServicePrincipal?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var brokerCalls = 0;
        var auth = new CoreAuthService((_, _, _) => { entered.TrySetResult(); return found.Task; },
            (_, _) => { brokerCalls++; return Task.FromResult("wrong-token"); });
        var network = new MetadataHandler();
        using var http = new HttpClient(new AuthenticatedHttpHandler(auth, () => active) { InnerHandler = network });
        var db = Path.Combine(Path.GetTempPath(), $"h01-catalog-{Guid.NewGuid():N}.db");
        var profiles = new ProfileStore(Path.Combine(Path.GetTempPath(), $"h01-profile-{Guid.NewGuid():N}.db"));
        var env = new FoEnvironment(snapshot.Id, snapshot.Name, snapshot.Url, snapshot.Tenant, snapshot.Legal)
        {
            MetadataCachePartition = EnvironmentIdentity.Create(snapshot).ToMetadataCachePartition(),
        };
        var service = new CatalogService(http, profiles, new CatalogStore(db));
        var pending = service.GetODataEntityIndexAsync(env, CatalogRefreshMode.UseCacheIfFresh, TestContext.Current.CancellationToken);
        await entered.Task;
        active = active with { ClientId = "client-b" };
        var intermediate = Principal(AuthTarget.Fo) with { ClientId = active.ClientId! };
        active = snapshot;
        found.TrySetResult(intermediate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(0, brokerCalls);
        Assert.Equal(0, network.Calls);

        // A new service must perform the good request: a poisoned persistent index would skip it.
        var goodAuth = new CoreAuthService((_, _, _) => Task.FromResult<ServicePrincipal?>(Principal(AuthTarget.Fo)),
            (_, _) => Task.FromResult("correct-token"));
        var goodNetwork = new MetadataHandler();
        using var goodHttp = new HttpClient(new AuthenticatedHttpHandler(goodAuth, () => active) { InnerHandler = goodNetwork });
        var restarted = new CatalogService(goodHttp, profiles, new CatalogStore(db));
        var index = await restarted.GetODataEntityIndexAsync(env, CatalogRefreshMode.UseCacheIfFresh, TestContext.Current.CancellationToken);
        Assert.Single(index.Entities);
        Assert.Equal(1, goodNetwork.Calls);
    }

    private sealed class MetadataHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx"><edmx:DataServices>
                <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
                <EntityType Name="Customer"><Key><PropertyRef Name="Id"/></Key><Property Name="Id" Type="Edm.String"/></EntityType>
                <EntityContainer Name="Container"><EntitySet Name="Customers" EntityType="Default.Customer"/></EntityContainer>
                </Schema></edmx:DataServices></edmx:Edmx>
                """) });
        }
    }
}
