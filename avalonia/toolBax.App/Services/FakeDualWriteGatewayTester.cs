using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>Design-mode / test <see cref="IDualWriteGatewayTester"/>: returns a canned result.</summary>
public sealed class FakeDualWriteGatewayTester : IDualWriteGatewayTester
{
    private readonly DwGatewayTestResult _result;

    public FakeDualWriteGatewayTester(DwGatewayTestResult? result = null) =>
        _result = result ?? new DwGatewayTestResult(true, "Linked: Contoso (cid 0e7b1f44…).");

    public Task<DwGatewayTestResult> TestAsync(EnvProfile env, CancellationToken ct = default) =>
        Task.FromResult(_result);
}
