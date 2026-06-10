using FoToolbox.Core.Auth;
using FoToolbox.Core.Profiles;
using System;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class VaultSecretReaderTests
{
    private static async Task<SecretVaultService> NewVaultAsync()
    {
        var store = new ProfileStore($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await store.EnsureCreatedAsync();
        return new SecretVaultService(store.ConnectionString);
    }

    [Fact]
    public async Task Reads_Typed_ClientSecretPayload()
    {
        var vault = await NewVaultAsync();
        var secretRef = await vault.StoreSecretAsync("ClientSecret", new ClientSecretPayload { Value = "s3cret" });
        Assert.Equal("s3cret", await VaultSecretReader.ReadClientSecretAsync(vault, secretRef, default));
    }

    [Fact]
    public async Task Reads_Raw_String_Secret_Avalonia_Shape()
    {
        var vault = await NewVaultAsync();
        var secretRef = await vault.StoreSecretAsync("fo-client-secret", "s3cret");
        Assert.Equal("s3cret", await VaultSecretReader.ReadClientSecretAsync(vault, secretRef, default));
    }

    [Fact]
    public async Task Reads_BearerTokenPayload()
    {
        var vault = await NewVaultAsync();
        var secretRef = await vault.StoreSecretAsync("BearerToken", new BearerTokenPayload { AccessToken = "abc.def.ghi" });
        var payload = await VaultSecretReader.ReadBearerTokenAsync(vault, secretRef, default);
        Assert.Equal("abc.def.ghi", payload?.AccessToken);
    }

    [Fact]
    public async Task Returns_Null_For_Missing_Ref()
    {
        var vault = await NewVaultAsync();
        Assert.Null(await VaultSecretReader.ReadClientSecretAsync(vault, Guid.NewGuid().ToString(), default));
    }
}
