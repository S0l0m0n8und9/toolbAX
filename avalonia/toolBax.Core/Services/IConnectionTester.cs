using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>Outcome of a "Test connection" probe: whether it succeeded and a human-readable message.</summary>
public sealed record ConnectionTestResult(bool Success, string Message);

/// <summary>
/// Probes F&amp;O <c>/data/$metadata</c> or Dataverse <c>/WhoAmI</c> with a fresh token.
/// Success confirms only that endpoint probe; it does not guarantee access to every tool or operation.
/// Behind an interface so the Profiles view-model stays headless-testable.
/// </summary>
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestFoAsync(EnvProfile env, CancellationToken ct = default);

    Task<ConnectionTestResult> TestDataverseAsync(EnvProfile env, CancellationToken ct = default);
}
