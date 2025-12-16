using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class ProfileStoreServicePrincipalTests
{
    [Fact]
    public async Task ServicePrincipal_With_BearerTokenMode_RoundTrips()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();

        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");
        await store.UpsertEnvironmentAsync(env);

        var sp = new ServicePrincipal("sp1", env.Id, string.Empty, AuthMode.BearerToken, "secretRef", null);
        await store.UpsertServicePrincipalAsync(sp);

        var sps = await store.GetServicePrincipalsAsync(env.Id);
        Assert.Single(sps);
        Assert.Equal(AuthMode.BearerToken, sps[0].AuthMode);
        Assert.Equal("secretRef", sps[0].SecretRef);
    }
}

