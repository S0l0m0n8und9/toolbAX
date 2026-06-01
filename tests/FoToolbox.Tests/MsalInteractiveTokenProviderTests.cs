using FoToolbox.Core.Auth;
using System;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public sealed class MsalInteractiveTokenProviderTests
{
    [Fact]
    public void BuildScope_AppendsDefaultScope()
    {
        Assert.Equal(
            "https://contoso.operations.dynamics.com/.default",
            MsalInteractiveTokenProvider.BuildScope("https://contoso.operations.dynamics.com"));
    }

    [Fact]
    public void BuildScope_TrimsTrailingSlashBeforeAppending()
    {
        Assert.Equal(
            "https://contoso.crm.dynamics.com/.default",
            MsalInteractiveTokenProvider.BuildScope("https://contoso.crm.dynamics.com/"));
    }

    [Fact]
    public void BuildAuthority_CombinesBaseAndTenant()
    {
        Assert.Equal(
            "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111",
            MsalInteractiveTokenProvider.BuildAuthority("https://login.microsoftonline.com/", "11111111-1111-1111-1111-111111111111"));
    }

    [Theory]
    [InlineData("", "tenant", "https://x.operations.dynamics.com", "Client ID")]
    [InlineData("client", "", "https://x.operations.dynamics.com", "Tenant")]
    [InlineData("client", "tenant", "", "URL")]
    public async Task AcquireTokenAsync_MissingRequiredInput_ThrowsBeforeBrowser(string clientId, string tenantId, string resourceBaseUrl, string expectedMessageFragment)
    {
        var provider = new MsalInteractiveTokenProvider();
        var request = new InteractiveTokenRequest(clientId, tenantId, resourceBaseUrl);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => provider.AcquireTokenAsync(request));
        Assert.Contains(expectedMessageFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
