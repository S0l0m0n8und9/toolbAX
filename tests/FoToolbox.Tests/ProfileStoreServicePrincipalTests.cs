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

        var foSp = new ServicePrincipal("sp1", env.Id, string.Empty, AuthMode.BearerToken, "secretRef", null, AuthTarget.Fo);
        var ceSp = new ServicePrincipal("sp2", env.Id, "ce-client", AuthMode.ClientSecret, "ceSecretRef", null, AuthTarget.Dataverse);
        await store.UpsertServicePrincipalAsync(foSp);
        await store.UpsertServicePrincipalAsync(ceSp);

        var sps = await store.GetServicePrincipalsAsync(env.Id);
        Assert.Equal(2, sps.Count);

        var loadedFo = await store.GetServicePrincipalAsync(env.Id, AuthTarget.Fo);
        Assert.NotNull(loadedFo);
        Assert.Equal(AuthMode.BearerToken, loadedFo!.AuthMode);
        Assert.Equal("secretRef", loadedFo.SecretRef);
        Assert.Equal(AuthTarget.Fo, loadedFo.Target);

        var loadedCe = await store.GetServicePrincipalAsync(env.Id, AuthTarget.Dataverse);
        Assert.NotNull(loadedCe);
        Assert.Equal(AuthMode.ClientSecret, loadedCe!.AuthMode);
        Assert.Equal("ce-client", loadedCe.ClientId);
        Assert.Equal(AuthTarget.Dataverse, loadedCe.Target);
    }
}
