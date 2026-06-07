using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteConnector"/>: acquires the delegated Data Integrator token, builds the
/// FoToolbox <see cref="DualWriteGatewayClient"/> against the configured (manual) gateway URL, and
/// resolves the connection (cid/cname). Mirrors <see cref="CoreDualWriteGatewayTester"/>'s connect path
/// but hands back the live gateway for the Operations screen. Network/MSAL integration — exercised
/// through the app, not unit tests (the VM uses <see cref="FakeDualWriteConnector"/>).
/// </summary>
public sealed class CoreDualWriteConnector : IDualWriteConnector
{
    private readonly IAuthService _auth;
    private readonly IDualWriteGatewayFactory _factory;

    public CoreDualWriteConnector(IAuthService auth, IDualWriteGatewayFactory? factory = null)
    {
        _auth = auth;
        _factory = factory ?? new DualWriteGatewayFactory();
    }

    public async Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.DualWriteGatewayUrl))
        {
            throw new InvalidOperationException("Set a dual-write gateway URL on the Data Integrator tab first.");
        }

        if (string.IsNullOrWhiteSpace(env.DataIntegratorClientId))
        {
            throw new InvalidOperationException("Set a Data Integrator client ID first.");
        }

        if (string.IsNullOrWhiteSpace(env.Url))
        {
            throw new InvalidOperationException("Set the F&O environment URL first.");
        }

        var token = await _auth.AcquireDualWriteTokenAsync(env, ct).ConfigureAwait(false);
        var settings = new DualWriteConnectionSettings(env.Id, env.DualWriteGatewayUrl, env.Url, token);
        var gateway = _factory.Create(settings);
        var linkage = await gateway.GetEnvironmentAsync(env.Url, ct).ConfigureAwait(false);
        return new DualWriteSession(gateway, linkage.Cid, linkage.Cname);
    }
}
