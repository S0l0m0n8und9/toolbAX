using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Acquires bearer tokens for an environment using its stored service principals (client credentials):
/// the F&amp;O app reg for OData/metadata, and the separate Dataverse app reg for the Dataverse Web API
/// (Dual-Write). Behind an interface so view-models can drive a "Test connection" and authenticated
/// calls against a fake without hitting Entra. Throws on failure (no token).
/// </summary>
public interface IAuthService
{
    Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default);

    Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default);
}
