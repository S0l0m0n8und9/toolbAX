using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Acquires an F&amp;O bearer token for an environment using its stored service principal (client
/// credentials). Behind an interface so the view-models can drive a "Test connection" and, later,
/// authenticated OData calls against a fake without hitting Entra. Throws on failure (no token).
/// </summary>
public interface IAuthService
{
    Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default);
}
