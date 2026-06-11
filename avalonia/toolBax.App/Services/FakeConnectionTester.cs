using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode / test <see cref="IConnectionTester"/>: returns a fixed result without any HTTP. Custom
/// delegates let negative tests simulate a failed probe.
/// </summary>
public sealed class FakeConnectionTester : IConnectionTester
{
    private readonly Func<EnvProfile, ConnectionTestResult> _fo;
    private readonly Func<EnvProfile, ConnectionTestResult> _dataverse;

    public FakeConnectionTester(
        Func<EnvProfile, ConnectionTestResult>? fo = null,
        Func<EnvProfile, ConnectionTestResult>? dataverse = null)
    {
        _fo = fo ?? (_ => new ConnectionTestResult(true, "F&O metadata reachable."));
        _dataverse = dataverse ?? (_ => new ConnectionTestResult(true, "Dataverse reachable."));
    }

    public Task<ConnectionTestResult> TestFoAsync(EnvProfile env, CancellationToken ct = default) =>
        Task.FromResult(_fo(env));

    public Task<ConnectionTestResult> TestDataverseAsync(EnvProfile env, CancellationToken ct = default) =>
        Task.FromResult(_dataverse(env));
}
