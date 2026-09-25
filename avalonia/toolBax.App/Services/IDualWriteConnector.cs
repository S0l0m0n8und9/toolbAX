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
/// <see cref="Profile"/> and <see cref="Identity"/> capture the immutable connection context the gateway was
/// established for. Every use site compares the complete identity with the current active environment before
/// issuing a call, so a same-id endpoint/auth/company edit cannot retarget an existing gateway session.
/// </summary>
public sealed class DualWriteSession
{
    public DualWriteSession(
        IDualWriteGateway gateway, string cid, string cname, EnvProfile profile, string gatewayBaseUrl = "")
    {
        Gateway = gateway;
        Cid = cid;
        Cname = cname;
        Profile = profile;
        Identity = EnvironmentIdentity.Create(profile);
        GatewayBaseUrl = gatewayBaseUrl;
    }

    public IDualWriteGateway Gateway { get; }
    public string Cid { get; }
    public string Cname { get; }
    public EnvProfile Profile { get; }
    public EnvironmentIdentity Identity { get; }
    public string EnvId => Profile.Id;
    public string GatewayBaseUrl { get; }
}

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
