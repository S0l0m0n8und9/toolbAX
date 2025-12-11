using FoToolbox.Core.Profiles;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class SecretVaultTests
{
    [Fact]
    public async Task Secret_Roundtrips_With_Protection()
    {
        var db = Path.GetTempFileName();
        var store = new FoToolbox.Core.Profiles.ProfileStore(db);
        await store.EnsureCreatedAsync();

        var vault = new SecretVaultService(store.ConnectionString);
        var payload = new SecretPayload("super-secret");
        var id = await vault.StoreSecretAsync("ClientSecret", payload);

        var read = await vault.ReadSecretAsync<SecretPayload>(id);
        Assert.NotNull(read);
        Assert.Equal("super-secret", read!.Value);
    }

    private sealed record SecretPayload(string Value);
}
