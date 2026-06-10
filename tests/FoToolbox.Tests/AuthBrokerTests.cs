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
}
