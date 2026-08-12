using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;
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
        var gateway = DualWriteGatewayWiring.CreateFor(_factory, settings);

        // The gateway owns an HttpClient, so dispose it unless we hand it to a valid session. This covers
        // both the empty-cid case and any exception from GetEnvironmentAsync (HTTP/network/cancel) — so a
        // retry after a transient failure doesn't leak a client each attempt.
        var handedOff = false;
        try
        {
            var linkage = await gateway.GetEnvironmentAsync(env.Url, ct).ConfigureAwait(false);

            // The gateway returns an empty cid (no exception) when this F&O environment isn't in a
            // dual-write connection set the resolved gateway knows about. Surface that here with an
            // actionable message rather than returning a session whose blank cid throws a cryptic
            // "cid is required" on the next call.
            if (!DualWriteConnectionGuard.IsLinked(linkage))
            {
                throw new InvalidOperationException(
                    DualWriteConnectionGuard.NoConnectionMessage(env.Url, result.GatewayBaseUrl));
            }

            // Stamped with the environment this connection was made for, so a later active-environment
            // switch can be detected by the Operations screen instead of acting on the wrong environment.
            var session = new DualWriteSession(gateway, linkage.Cid, linkage.Cname, env.Id, result.GatewayBaseUrl);
            handedOff = true;
            return session;
        }
        finally
        {
            if (!handedOff)
            {
                (gateway as IDisposable)?.Dispose();
            }
        }
    }
}

/// <summary>
/// Shared gateway-client wiring for the two entry points that sign in and then talk to the gateway
/// (<see cref="CoreDualWriteConnector"/> and <see cref="CoreDualWriteGatewayTester"/>).
/// </summary>
internal static class DualWriteGatewayWiring
{
    /// <summary>
    /// Builds the gateway client for a freshly signed-in session, preferring the <em>renewing</em> client
    /// whenever the sign-in also yielded a refresh token. The delegated Data Integrator access token lasts
    /// about an hour; with the static-bearer client every operation past that point failed with a bare 401
    /// and the only way back was another browser sign-in.
    /// </summary>
    public static IDualWriteGateway CreateFor(IDualWriteGatewayFactory factory, DualWriteConnectionSettings settings) =>
        settings.HasDelegatedSession
            ? factory.CreateRefreshing(settings, LogRenewal)
            : factory.Create(settings);

    // Nothing persists dual-write tokens today (they live for the lifetime of the session), so this only
    // records that a renewal happened — deliberately the expiry only, never any part of the token itself.
    private static Task LogRenewal(DualWriteToken token)
    {
        Trace.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Dual-write gateway token renewed; now valid to {0:O}.",
            token.ExpiresUtc));
        return Task.CompletedTask;
    }
}
