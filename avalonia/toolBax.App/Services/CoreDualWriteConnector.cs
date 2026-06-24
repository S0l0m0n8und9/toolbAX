using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteConnector"/>: drives the Data Integrator portal sign-in (via
/// <see cref="IDualWriteSignIn"/>) to capture the delegated token <em>and</em> the auto-discovered
/// regional gateway host, then builds the FoToolbox <see cref="DualWriteGatewayClient"/> and resolves the
/// connection (cid/cname). Mirrors the WPF plugin's flow exactly — no user-supplied client id, no
/// manually-entered gateway URL. Network/MSAL/WebView integration — exercised through the app, not unit
/// tests (the VM uses <see cref="FakeDualWriteConnector"/>).
/// </summary>
public sealed class CoreDualWriteConnector : IDualWriteConnector
{
    private readonly IDualWriteSignIn _signIn;
    private readonly IDualWriteGatewayFactory _factory;

    public CoreDualWriteConnector(IDualWriteSignIn signIn, IDualWriteGatewayFactory? factory = null)
    {
        _signIn = signIn;
        _factory = factory ?? new DualWriteGatewayFactory();
    }

    public async Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Url))
        {
            throw new InvalidOperationException("Set the F&O environment URL first.");
        }

        // Portal sign-in yields BOTH the delegated token and the regional gateway host (no client id /
        // gateway URL to configure) — the gateway host is discovered, not taken from the profile.
        var result = await _signIn.SignInAsync(env, switchAccount: false, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Data Integrator sign-in was cancelled or did not complete.");

        var settings = new DualWriteConnectionSettings(env.Id, result.GatewayBaseUrl, env.Url, result.Token.AccessToken)
        {
            RefreshToken = result.Token.RefreshToken,
            AccessTokenExpiryUtc = result.Token.ExpiresUtc,
        };
        var gateway = _factory.Create(settings);
        var linkage = await gateway.GetEnvironmentAsync(env.Url, ct).ConfigureAwait(false);

        // The gateway returns an empty cid (no exception) when this F&O environment isn't in a dual-write
        // connection set the resolved gateway knows about. Surface that here with an actionable message
        // rather than returning a session whose blank cid throws a cryptic "cid is required" on the next
        // call. Dispose the gateway we built (it owns an HttpClient) since we're not returning it.
        if (!DualWriteConnectionGuard.IsLinked(linkage))
        {
            (gateway as IDisposable)?.Dispose();
            throw new InvalidOperationException(
                DualWriteConnectionGuard.NoConnectionMessage(env.Url, result.GatewayBaseUrl));
        }

        return new DualWriteSession(gateway, linkage.Cid, linkage.Cname);
    }
}
