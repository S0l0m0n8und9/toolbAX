using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class AuthBrokerTests
{
    [Fact]
    [Trait("Category", "Auth")]
    public async Task Interactive_AuthMode_RoundTrips_Through_ProfileStore()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        var svc = new ProfileService(store);
        await svc.EnsureCreatedAsync();
        await svc.UpsertEnvironmentAsync(new FoEnvironment("env1", "Env", "https://contoso.operations.dynamics.com", "tenant", null));
        await svc.UpsertServicePrincipalAsync(new ServicePrincipal("sp1", "env1", "client-id", AuthMode.Interactive, null, null, AuthTarget.Fo));

        var loaded = await svc.GetServicePrincipalAsync("env1", AuthTarget.Fo);

        Assert.Equal(AuthMode.Interactive, loaded!.AuthMode);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public void MsalTokenProvider_Reuses_App_For_Same_Credential_And_Rebuilds_On_Rotation()
    {
        var provider = new FoToolbox.Core.Auth.MsalTokenProvider(
            "https://login.microsoftonline.com",
            (_, _) => Task.FromResult<FoToolbox.Core.Auth.ClientCredential>(new FoToolbox.Core.Auth.ClientSecretCredential("secret-1")));

        var sp = new ServicePrincipal("sp", "env", "client-id", AuthMode.ClientSecret, null, null);
        var authority = "https://login.microsoftonline.com/tenant";
        var cred1 = new FoToolbox.Core.Auth.ClientSecretCredential("secret-1");
        var cred2 = new FoToolbox.Core.Auth.ClientSecretCredential("secret-2");

        var app1 = provider.GetOrCreateApp(sp, authority, cred1);
        var app2 = provider.GetOrCreateApp(sp, authority, cred1);
        var app3 = provider.GetOrCreateApp(sp, authority, cred2);
        // different authority (tenant) → different app entry
        var app4 = provider.GetOrCreateApp(sp, "https://login.microsoftonline.com/other-tenant", cred1);

        Assert.Same(app1, app2);
        Assert.NotSame(app1, app3);
        Assert.NotSame(app1, app4);
    }
}
