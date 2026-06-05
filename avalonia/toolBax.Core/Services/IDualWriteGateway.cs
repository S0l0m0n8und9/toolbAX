using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

public enum RequestPhase
{
    Posting,
    InProgress,
    Succeeded,
    Failed,
}

/// <summary>Polled result of a submitted action: phase + the current state of each affected map.</summary>
public sealed record GatewayStatus(
    string RequestId,
    RequestPhase Phase,
    IReadOnlyDictionary<string, MapState> MapStates);

/// <summary>
/// The Dual-Write Management gateway seam. The only place that talks to the live gateway; ViewModels
/// and tests run against a fake. <see cref="SubmitActionAsync"/> returns a request id the caller polls
/// via <see cref="GetStatusAsync"/> until a terminal phase.
/// </summary>
public interface IDualWriteGateway
{
    Task<GatewayInfo> ResolveEnvironmentAsync(string identifier, CancellationToken ct);

    Task<IReadOnlyList<DwMap>> GetMapsAsync(string cid, CancellationToken ct);

    Task<string> SubmitActionAsync(string cid, DwAction action, IReadOnlyList<string> tableIds, CancellationToken ct);

    Task<GatewayStatus> GetStatusAsync(string requestId, CancellationToken ct);
}
