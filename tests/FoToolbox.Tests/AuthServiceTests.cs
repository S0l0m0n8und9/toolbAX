using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using Microsoft.Identity.Client;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class AuthServiceTests
{
    private sealed class FakeTokenProvider : ITokenProvider
    {
        private readonly string _token;
        private readonly int _failures;
        private int _callCount;
        public TokenRequest? LastRequest { get; private set; }

        public FakeTokenProvider(string token, int failures = 0)
        {
            _token = token;
            _failures = failures;
        }

        public Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            _callCount++;
            if (_callCount <= _failures)
            {
                throw new InvalidOperationException("Transient");
            }

            return Task.FromResult(_token);
        }
    }

    private sealed class FailingTokenProvider : ITokenProvider
    {
        private readonly Exception _failure;

        public FailingTokenProvider(Exception failure)
        {
            _failure = failure;
        }

        public Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
        {
            throw _failure;
        }
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Uses_TokenProvider()
    {
        var env = new FoEnvironment("id", "name", "https://example.operations.dynamics.com", "tenant", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.ClientSecret, "secretRef", null);

        var provider = new FakeTokenProvider("token-123");
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync(env, sp);
        Assert.Equal("token-123", token);
        Assert.Equal("https://example.operations.dynamics.com/.default", provider.LastRequest?.Scope);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Retries_On_Failure()
    {
        var env = new FoEnvironment("id", "name", "https://example.operations.dynamics.com", "tenant", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.ClientSecret, "secretRef", null);

        var provider = new FakeTokenProvider("token-456", failures: 1);
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync(env, sp);
        Assert.Equal("token-456", token);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Overload_Uses_Provided_Resource_BaseUrl()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FakeTokenProvider("token-789");
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp);
        Assert.Equal("token-789", token);
        Assert.Equal("https://org.crm.dynamics.com/.default", provider.LastRequest?.Scope);
        Assert.Equal("tenant", provider.LastRequest?.TenantId);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Uses_Environment_TenantId_For_Authority_Resolution()
    {
        var env = new FoEnvironment("id", "name", "https://example.operations.dynamics.com", "tenant-guid-or-domain", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.ClientSecret, "secretRef", null);

        var provider = new FakeTokenProvider("token-123");
        var svc = new AuthService(provider);

        _ = await svc.AcquireTokenAsync(env, sp);

        Assert.Equal(env.TenantId, provider.LastRequest?.TenantId);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_InvalidGrant_Throws_Reauth_Message()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalServiceException("invalid_grant", "refresh token expired"));
        AuthRecoveryException? prompted = null;
        var svc = new AuthService(provider, "Finance and Operations", recovery => prompted = recovery);

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));
        Assert.Contains("switch to Profiles", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Finance and Operations", failure.ServiceName);
        Assert.Equal("Finance and Operations sign-in required", failure.PromptTitle);
        Assert.True(failure.RequiresInteractiveReauth);
        Assert.Same(failure, prompted);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_UiRequired_Throws_Reauth_Message()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalUiRequiredException("user_null", "user interaction required"));
        AuthRecoveryException? prompted = null;
        var svc = new AuthService(provider, "Dataverse", recovery => prompted = recovery);

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));
        Assert.Contains("interactive sign-in", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Dataverse", failure.ServiceName);
        Assert.IsType<MsalUiRequiredException>(failure.InnerException);
        Assert.True(failure.RequiresInteractiveReauth);
        Assert.Same(failure, prompted);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_ExpiredRefreshTokenMessage_Throws_Reauth_Message()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalServiceException("temporarily_unavailable", "The refresh token has expired and user interaction is required."));
        var svc = new AuthService(provider, "Finance and Operations");

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));
        Assert.Contains("interactive sign-in", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<MsalServiceException>(failure.InnerException);
    }
}
