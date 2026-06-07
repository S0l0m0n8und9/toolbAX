using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using ToolBax.App.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Exercises <see cref="CoreInteractiveAuthBroker"/> over a fake <see cref="IInteractiveTokenProvider"/>:
/// it requests the Data Integrator (IntegratorApp) delegated resource with the env's client id/tenant,
/// reports the signed-in account from the token, and returns null on cancellation. No real browser/MSAL.
/// </summary>
public class CoreInteractiveAuthBrokerTests
{
    private static string Jwt(string payloadJson)
    {
        static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payloadJson)}.sig";
    }

    private sealed class FakeProvider : IInteractiveTokenProvider
    {
        private readonly string _token;
        private readonly bool _cancel;
        public InteractiveTokenRequest? LastRequest { get; private set; }
        public FakeProvider(string token, bool cancel = false) { _token = token; _cancel = cancel; }
        public Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (_cancel)
            {
                throw new OperationCanceledException();
            }

            return Task.FromResult(new InteractiveTokenResult(_token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    [Fact]
    public async Task SignIn_requests_the_integrator_resource_and_reports_the_account()
    {
        var provider = new FakeProvider(Jwt("{\"preferred_username\":\"svc@contoso.com\"}"));
        var broker = new CoreInteractiveAuthBroker(provider);

        var result = await broker.SignInAsync("client-123", "tenant-abc", TestContext.Current.CancellationToken);

        Assert.Equal("svc@contoso.com", result!.Account);
        Assert.Equal("client-123", provider.LastRequest!.ClientId);
        Assert.Equal("tenant-abc", provider.LastRequest.TenantId);
        Assert.Equal("https://IntegratorApp.com", provider.LastRequest.ResourceBaseUrl);
    }

    [Fact]
    public async Task SignIn_falls_back_to_a_generic_account_when_no_username_claim()
    {
        var broker = new CoreInteractiveAuthBroker(new FakeProvider(Jwt("{\"sub\":\"1\"}")));

        var result = await broker.SignInAsync("c", "t", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.Account));
    }

    [Fact]
    public async Task SignIn_returns_null_on_cancellation()
    {
        var broker = new CoreInteractiveAuthBroker(new FakeProvider("x", cancel: true));

        var result = await broker.SignInAsync("c", "t", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
