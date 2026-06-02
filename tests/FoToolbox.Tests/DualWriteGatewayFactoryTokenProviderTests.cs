using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteGatewayFactoryTokenProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastAuth;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuth = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task CreateWithTokenProvider_AttachesProviderToken()
    {
        var inner = new CapturingHandler();
        var factory = new DualWriteGatewayFactory();
        var token = "abc";
        var gateway = factory.CreateWithTokenProvider(
            "https://projectmanagementservice.au-il102.gateway.prod.island.powerapps.com",
            _ => Task.FromResult(token),
            innerHandler: inner);

        await gateway.GetEnvironmentAsync("https://x.operations.dynamics.com", CancellationToken.None);

        Assert.Equal("abc", inner.LastAuth);
    }
}
