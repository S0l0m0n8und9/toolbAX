using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>Outcome of a dual-write gateway connection test.</summary>
public sealed record DwGatewayTestResult(bool IsSuccess, string Message);

/// <summary>
/// Tests the dual-write management gateway connection for an environment: acquires the delegated token,
/// builds the gateway client against the configured gateway URL, and resolves the F&amp;O linkage. Behind
/// an interface so the Profiles view-model can drive a "Test gateway" command against a fake.
/// </summary>
public interface IDualWriteGatewayTester
{
    Task<DwGatewayTestResult> TestAsync(EnvProfile env, CancellationToken ct = default);
}
