using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteCompareService"/>: connects to each environment's dual-write gateway (via
/// <see cref="IDualWriteConnector"/>, reusing the Operations connect path), loads both map sets, and
/// diffs them with the pure Core <see cref="DualWriteMapComparer"/>. Network/MSAL integration —
/// exercised through the app; the VM tests use a fake.
/// </summary>
public sealed class CoreDualWriteCompareService : IDualWriteCompareService
{
    private readonly IDualWriteConnector _connector;

    public CoreDualWriteCompareService(IDualWriteConnector connector) => _connector = connector;

    public async Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(EnvProfile source, EnvProfile target, CancellationToken ct = default)
    {
        // Sign-ins run SEQUENTIALLY, source fully signed in before target's starts: connecting opens a
        // MODAL browser sign-in window (the Data Integrator portal), and only one of those can be up at a
        // time. Run in parallel, two modal windows stack on the same owner — closing one re-enables the
        // main window while the other is still modal, and a manually-closed window's best-effort token
        // capture can be attributed to the wrong environment. One sign-in at a time, each named after the
        // environment it belongs to (see DualWriteSignInTitle). The extra wall-clock cost is a round-trip
        // the user spends signing in anyway.
        var sourceSession = await _connector.ConnectAsync(source, ct).ConfigureAwait(false);
        DualWriteSession targetSession;
        try
        {
            targetSession = await _connector.ConnectAsync(target, ct).ConfigureAwait(false);
        }
        catch
        {
            // Source is already signed in and its gateway is live — if target's sign-in then fails, that
            // gateway would otherwise leak (unlike the old fully-sequential flow, where source's own load
            // — and dispose — always finished before target's connect was even attempted).
            DisposeGateway(sourceSession);
            throw;
        }

        // Once both sides are signed in, loading maps is plain headless HTTP — no modal, no attribution
        // risk — so the two loads can run in parallel. Each load disposes its own gateway in a finally
        // regardless of outcome, so one side's load failing still leaves both gateways cleaned up.
        var sourceLoad = LoadAsync(sourceSession, ct);
        var targetLoad = LoadAsync(targetSession, ct);
        await Task.WhenAll(sourceLoad, targetLoad).ConfigureAwait(false);

        return DualWriteMapComparer.Compare(sourceLoad.Result, targetLoad.Result);
    }

    private static async Task<IReadOnlyList<DualWriteMap>> LoadAsync(DualWriteSession session, CancellationToken ct)
    {
        try
        {
            return await session.Gateway.GetMapsAsync(session.Cid, ct).ConfigureAwait(false);
        }
        finally
        {
            DisposeGateway(session);
        }
    }

    private static void DisposeGateway(DualWriteSession session)
    {
        if (session.Gateway is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
