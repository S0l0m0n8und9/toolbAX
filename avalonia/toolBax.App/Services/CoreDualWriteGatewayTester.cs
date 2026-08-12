using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteGatewayTester"/>: drives the Data Integrator portal sign-in (via
/// <see cref="IDualWriteSignIn"/>) to capture the delegated token + auto-discovered gateway host, builds
/// the FoToolbox <see cref="DualWriteGatewayClient"/>, and resolves the F&amp;O dual-write linkage as a
/// connection check — no user-supplied client id, no manual gateway URL. Network/MSAL/WebView
/// integration — exercised through the app, not unit tests (the VM uses <see cref="FakeDualWriteGatewayTester"/>).
/// </summary>
public sealed class CoreDualWriteGatewayTester : IDualWriteGatewayTester
{
    private readonly IDualWriteSignIn _signIn;
    private readonly IDualWriteGatewayFactory _factory;

    public CoreDualWriteGatewayTester(IDualWriteSignIn signIn, IDualWriteGatewayFactory? factory = null)
    {
        _signIn = signIn;
        _factory = factory ?? new DualWriteGatewayFactory();
    }

    public async Task<DwGatewayTestResult> TestAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Url))
        {
            return new DwGatewayTestResult(false, "Set the F&O environment URL first.");
        }

        try
        {
            var result = await _signIn.SignInAsync(env, switchAccount: false, ct).ConfigureAwait(false);
            if (result is null)
            {
                return new DwGatewayTestResult(false, "Data Integrator sign-in was cancelled or did not complete.");
            }

            var settings = new DualWriteConnectionSettings(env.Id, result.GatewayBaseUrl, env.Url, result.Token.AccessToken)
            {
                RefreshToken = result.Token.RefreshToken,
                AccessTokenExpiryUtc = result.Token.ExpiresUtc,
            };
            // Same renewing-client preference as CoreDualWriteConnector: the test is a single call, but it
            // must exercise the client the app will actually use, not a shape that only the tester sees.
            var gateway = DualWriteGatewayWiring.CreateFor(_factory, settings);
            DualWriteEnvironment linkage;
            try
            {
                linkage = await gateway.GetEnvironmentAsync(env.Url, ct).ConfigureAwait(false);
            }
            finally
            {
                // The tester only checks the linkage; it never keeps the gateway, so dispose its HttpClient.
                (gateway as IDisposable)?.Dispose();
            }

            // An empty cid means the gateway found no connection for this environment — report that as a
            // failure instead of a misleading "Linked: (unnamed) (cid )." success.
            if (!DualWriteConnectionGuard.IsLinked(linkage))
            {
                return new DwGatewayTestResult(false,
                    DualWriteConnectionGuard.NoConnectionMessage(env.Url, result.GatewayBaseUrl));
            }

            var name = string.IsNullOrWhiteSpace(linkage.Cname) ? "(unnamed)" : linkage.Cname;
            return new DwGatewayTestResult(true, $"Linked: {name} (cid {linkage.Cid}). Gateway: {result.GatewayBaseUrl}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DwGatewayTestResult(false, $"Gateway test failed: {ex.Message}");
        }
    }
}
