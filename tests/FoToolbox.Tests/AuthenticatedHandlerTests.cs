using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using FoToolbox.Host;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class AuthenticatedHandlerTests
{
    [Trait("Category", "Auth")]
    [Fact]
    public async Task SendAsync_Notifies_ReauthCoordinator_When_Service_Rejects_Credentials()
    {
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "contoso.onmicrosoft.com", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.BearerToken, null, null);
        var coordinator = new AuthReauthCoordinator();
        AuthRecoveryException? notified = null;
        coordinator.ReauthRequired += ex => notified = ex;

        var handler = new AuthenticatedHandler(env, sp, new SecretVaultService($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared"), coordinator)
        {
            InnerHandler = new UnauthorizedHandler()
        };
        Environment.SetEnvironmentVariable("FOTB_BEARER_TOKEN", CreateJwtToken(DateTimeOffset.UtcNow.AddMinutes(5)));

        try
        {
            using var http = new HttpClient(handler);
            var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => http.GetAsync("https://contoso.operations.dynamics.com/data"));

            Assert.NotNull(notified);
            Assert.Equal(failure.ReauthMessage, notified!.ReauthMessage);
            Assert.Contains("interactive re-authentication", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(failure.RequiresInteractiveReauth);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTB_BEARER_TOKEN", null);
        }
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task SendAsync_Expired_BearerToken_Shows_Reauth_Message_Instead_Of_401()
    {
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "contoso.onmicrosoft.com", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.BearerToken, null, null);
        var coordinator = new AuthReauthCoordinator();
        AuthRecoveryException? notified = null;
        coordinator.ReauthRequired += ex => notified = ex;

        var handler = new AuthenticatedHandler(env, sp, new SecretVaultService($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared"), coordinator)
        {
            InnerHandler = new UnexpectedCallHandler()
        };
        Environment.SetEnvironmentVariable("FOTB_BEARER_TOKEN", CreateJwtToken(DateTimeOffset.UtcNow.AddMinutes(-5)));

        try
        {
            using var http = new HttpClient(handler);
            var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => http.GetAsync("https://contoso.operations.dynamics.com/data"));

            Assert.Contains("expired", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(notified);
            Assert.Same(failure, notified);
            Assert.True(failure.RequiresInteractiveReauth);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTB_BEARER_TOKEN", null);
        }
    }

    private sealed class UnauthorizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }

    private sealed class UnexpectedCallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new Xunit.Sdk.XunitException("HTTP pipeline should not be invoked when local auth validation fails.");
        }
    }

    private static string CreateJwtToken(DateTimeOffset expiry)
    {
        static string Encode(string json)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        var header = Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Encode($"{{\"exp\":{expiry.ToUnixTimeSeconds()}}}");
        return $"{header}.{payload}.signature";
    }
}
