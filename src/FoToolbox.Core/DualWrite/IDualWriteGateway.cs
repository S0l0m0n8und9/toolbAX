using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Abstraction over the Dual-write Management gateway so consumers (plugin view-models)
/// can be unit-tested without real HTTP. Implemented by <see cref="DualWriteGatewayClient"/>.
/// </summary>
public interface IDualWriteGateway
{
    Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default);
    Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default);
    Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default);
}
