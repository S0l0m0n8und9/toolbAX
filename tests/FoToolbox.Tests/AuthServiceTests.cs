using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
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

        public FakeTokenProvider(string token, int failures = 0)
        {
            _token = token;
            _failures = failures;
        }

        public Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount <= _failures)
            {
                throw new InvalidOperationException("Transient");
            }

            return Task.FromResult(_token);
        }
    }

    [Fact]
    public async Task AcquireToken_Uses_TokenProvider()
    {
        var env = new FoEnvironment("id", "name", "https://example.operations.dynamics.com", "tenant", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.ClientSecret, "secretRef", null);

        var provider = new FakeTokenProvider("token-123");
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync(env, sp);
        Assert.Equal("token-123", token);
    }

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
}
