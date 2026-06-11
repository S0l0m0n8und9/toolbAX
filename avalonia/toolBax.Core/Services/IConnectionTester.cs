using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>Outcome of a "Test connection" probe: whether it succeeded and a human-readable message.</summary>
public sealed record ConnectionTestResult(bool Success, string Message);

/// <summary>
/// Verifies that a profile can actually reach its data endpoint — not just that a token can be minted.
/// The real implementation forces a fresh token and calls the same endpoint the tools use (F&amp;O
/// <c>/data/$metadata</c>, Dataverse <c>/WhoAmI</c>), so a green test means the tool screens will load.
/// Behind an interface so the Profiles view-model stays headless-testable.
/// </summary>
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestFoAsync(EnvProfile env, CancellationToken ct = default);

    Task<ConnectionTestResult> TestDataverseAsync(EnvProfile env, CancellationToken ct = default);
}
