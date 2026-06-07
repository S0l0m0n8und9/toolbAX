using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteGatewayTester"/>: acquires the delegated Data Integrator token, builds the
/// FoToolbox <see cref="DualWriteGatewayClient"/> against the configured gateway URL (manual host, per
/// the loopback path), and resolves the F&amp;O dual-write linkage as a connection check. Network/MSAL
/// integration — exercised through the app, not unit tests (the VM uses <see cref="FakeDualWriteGatewayTester"/>).
/// </summary>
public sealed class CoreDualWriteGatewayTester : IDualWriteGatewayTester
{
    private readonly IAuthService _auth;
    private readonly IDualWriteGatewayFactory _factory;

    public CoreDualWriteGatewayTester(IAuthService auth, IDualWriteGatewayFactory? factory = null)
    {
        _auth = auth;
        _factory = factory ?? new DualWriteGatewayFactory();
    }

    public async Task<DwGatewayTestResult> TestAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.DualWriteGatewayUrl))
        {
            return new DwGatewayTestResult(false, "Set a dual-write gateway URL on the Data Integrator tab first.");
        }

        if (string.IsNullOrWhiteSpace(env.DataIntegratorClientId))
        {
            return new DwGatewayTestResult(false, "Set a Data Integrator client ID first.");
        }

        if (string.IsNullOrWhiteSpace(env.Url))
        {
            return new DwGatewayTestResult(false, "Set the F&O environment URL first.");
        }

        try
        {
            var token = await _auth.AcquireDualWriteTokenAsync(env, ct).ConfigureAwait(false);
            var settings = new DualWriteConnectionSettings(env.Id, env.DualWriteGatewayUrl, env.Url, token);
            var gateway = _factory.Create(settings);
            var linkage = await gateway.GetEnvironmentAsync(env.Url, ct).ConfigureAwait(false);

            var name = string.IsNullOrWhiteSpace(linkage.Cname) ? "(unnamed)" : linkage.Cname;
            return new DwGatewayTestResult(true, $"Linked: {name} (cid {linkage.Cid}).");
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
