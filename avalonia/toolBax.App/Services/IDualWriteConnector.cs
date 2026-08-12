using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// A live dual-write gateway connection: the connected <see cref="IDualWriteGateway"/> plus the resolved
/// connection id/name for the environment, and the (auto-discovered) gateway host it's bound to.
/// <see cref="Cid"/> is passed to the gateway's map/action calls; <see cref="GatewayBaseUrl"/> is surfaced
/// in the in-app gateway log so a wrong/region host is visible.
/// <see cref="EnvId"/> stamps the <see cref="EnvProfile.Id"/> the session was established for: the shell can
/// switch the active environment under a cached tool view-model (the user may decline the "refresh open
/// tools?" prompt), so every use site compares this against the *current* active environment before issuing
/// a call — otherwise a session bound to environment A would act while the header shows environment B.
/// </summary>
public sealed record DualWriteSession(
    IDualWriteGateway Gateway, string Cid, string Cname, string EnvId, string GatewayBaseUrl = "");

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
