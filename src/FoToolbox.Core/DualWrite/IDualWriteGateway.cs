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

    /// <summary>Activates a specific template version for a map (the "apply map version" action).</summary>
    Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId, string templateId, CancellationToken cancellationToken = default);

    /// <summary>Lists the field mappings for a project (the units "refresh tables" operates on).</summary>
    Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the table/entity metadata for a project field mapping.</summary>
    Task RefreshTablesAsync(string fieldMappingName, CancellationToken cancellationToken = default);
}
