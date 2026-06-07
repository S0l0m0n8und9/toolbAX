using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode / test <see cref="IDualWriteConnector"/>: returns a session over an in-memory
/// <see cref="FakeCoreDualWriteGateway"/> seeded with representative maps, so the Operations screen
/// renders without a live gateway. A null <c>maps</c> uses the seed; an empty list exercises the
/// empty state.
/// </summary>
public sealed class FakeDualWriteConnector : IDualWriteConnector
{
    private readonly IReadOnlyList<DualWriteMap>? _maps;
    private readonly Exception? _failWith;

    public FakeDualWriteConnector(IReadOnlyList<DualWriteMap>? maps = null) => _maps = maps;

    private FakeDualWriteConnector(Exception failWith) => _failWith = failWith;

    /// <summary>A connector whose <see cref="ConnectAsync"/> always throws, to drive the error state.</summary>
    public static FakeDualWriteConnector ThatFails(string message) =>
        new(new InvalidOperationException(message));

    public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (_failWith is not null)
        {
            throw _failWith;
        }

        var gateway = new FakeCoreDualWriteGateway(_maps ?? SeedMaps());
        return Task.FromResult(new DualWriteSession(gateway, "fake-cid", "Contoso (AUMF · APAC Prod)"));
    }

    public static IReadOnlyList<DualWriteMap> SeedMaps() => new[]
    {
        Map("Customers V3", "account", "Running", "1.0.0.12", "Microsoft"),
        Map("Vendors V2", "msdyn_vendor", "Running", "1.0.0.8", "Microsoft"),
        Map("Released products V2", "product", "Paused", "1.0.0.21", "contoso.it"),
        Map("Sales order headers", "salesorder", "Running", "1.0.0.15", "contoso.it"),
        Map("Chart of accounts", "msdyn_coa", "Stopped", "1.0.0.3", "Microsoft"),
    };

    private static DualWriteMap Map(string name, string ceEntity, string state, string version, string author)
    {
        var template = new DualWriteTemplate($"tpl-{ceEntity}", version, author);
        return new DualWriteMap(
            Id: $"map-{ceEntity}",
            Name: name,
            DisplayName: name,
            ProjectId: $"proj-{ceEntity}",
            State: state,
            ActiveTemplate: template,
            Templates: new[] { template })
        {
            RightEntityName = ceEntity,
        };
    }
}

/// <summary>In-memory <see cref="IDualWriteGateway"/> for design-mode/tests — only the read methods the
/// Operations screen uses are implemented; the rest throw until they're needed (lifecycle actions).</summary>
public sealed class FakeCoreDualWriteGateway : IDualWriteGateway
{
    private readonly IReadOnlyList<DualWriteMap> _maps;

    public FakeCoreDualWriteGateway(IReadOnlyList<DualWriteMap> maps) => _maps = maps;

    public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default)
        => Task.FromResult(new DualWriteEnvironment("fake-cid", "Contoso (AUMF · APAC Prod)", foIdentifier));

    public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default)
        => Task.FromResult(_maps);

    public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Lifecycle actions are not part of the read-path fake.");

    public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Lifecycle actions are not part of the read-path fake.");

    public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId, string templateId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task RefreshTablesAsync(string fieldMappingName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities, bool forceReset, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName, IReadOnlyList<string> keyFields, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
