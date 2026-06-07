using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.DualWrite.Auth;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IInteractiveAuthBroker"/>: acquires a delegated Data Integrator / dual-write token via
/// FoToolbox's loopback MSAL provider (system browser, no WebView2) for the env's client id + tenant, and
/// reports the signed-in account from the token. The token is cached by the provider so a later silent
/// acquisition can build the dual-write gateway connection. Returns null when the user cancels.
/// </summary>
public sealed class CoreInteractiveAuthBroker : IInteractiveAuthBroker
{
    private readonly IInteractiveTokenProvider _provider;

    public CoreInteractiveAuthBroker(IInteractiveTokenProvider? provider = null) =>
        _provider = provider ?? new MsalInteractiveTokenProvider();

    public async Task<AuthResult?> SignInAsync(string clientId, string tenant, CancellationToken ct = default)
    {
        try
        {
            var result = await _provider
                .AcquireTokenAsync(new InteractiveTokenRequest(clientId, tenant, DualWriteAuthConstants.ResourceBaseUrl), ct)
                .ConfigureAwait(false);

            return new AuthResult(JwtClaimReader.ReadUsername(result.AccessToken) ?? "signed in");
        }
        catch (OperationCanceledException)
        {
            return null; // user dismissed the browser sign-in
        }
    }
}
