using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// A live dual-write gateway connection: the connected <see cref="IDualWriteGateway"/> plus the resolved
/// connection id/name for the environment. <see cref="Cid"/> is passed to the gateway's map/action calls.
/// </summary>
public sealed record DualWriteSession(IDualWriteGateway Gateway, string Cid, string Cname);

/// <summary>
/// Establishes a dual-write gateway session for an environment: acquires the delegated token, builds the
/// gateway client (manual host + bearer), and resolves the connection (cid/cname). The Operations screen
/// uses this instead of re-porting the gateway — the real client is <c>FoToolbox.Core</c>'s
/// <see cref="DualWriteGatewayClient"/>. Implementations throw a message-bearing exception on failure.
/// </summary>
public interface IDualWriteConnector
{
    Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default);
}
