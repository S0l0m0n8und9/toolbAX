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
        // The two environments are independent gateway round-trips — connect + load them in parallel.
        var leftTask = ConnectAndLoadAsync(source, ct);
        var rightTask = ConnectAndLoadAsync(target, ct);
        await Task.WhenAll(leftTask, rightTask).ConfigureAwait(false);
        return DualWriteMapComparer.Compare(await leftTask.ConfigureAwait(false), await rightTask.ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<DualWriteMap>> ConnectAndLoadAsync(EnvProfile env, CancellationToken ct)
    {
        var session = await _connector.ConnectAsync(env, ct).ConfigureAwait(false);
        try
        {
            return await session.Gateway.GetMapsAsync(session.Cid, ct).ConfigureAwait(false);
        }
        finally
        {
            if (session.Gateway is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
