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
    private readonly Task? _gate;
    private readonly int _pollsBeforeTerminal;
    private readonly bool _emptyRequestId;

    /// <summary>The gateway handed out by the last successful connect (for asserting on actions).</summary>
    public FakeCoreDualWriteGateway? LastGateway { get; private set; }

    private readonly int _failGetMapsOnCall;

    public FakeDualWriteConnector(
        IReadOnlyList<DualWriteMap>? maps = null,
        int pollsBeforeTerminal = 0,
        int failGetMapsOnCall = 0,
        bool emptyRequestId = false)
    {
        _maps = maps;
        _pollsBeforeTerminal = pollsBeforeTerminal;
        _failGetMapsOnCall = failGetMapsOnCall;
        _emptyRequestId = emptyRequestId;
    }

    private FakeDualWriteConnector(Exception failWith) => _failWith = failWith;

    private FakeDualWriteConnector(Task gate) => _gate = gate;

    /// <summary>A connector whose <see cref="ConnectAsync"/> always throws, to drive the error state.</summary>
    public static FakeDualWriteConnector ThatFails(string message) =>
        new(new InvalidOperationException(message));

    /// <summary>
    /// A connector that reports cancellation the way an HTTP/socket timeout does — an
    /// <see cref="OperationCanceledException"/> raised while the caller's own token is still live. That is
    /// NOT a user cancel, so the screen must not report it as one (#166).
    /// </summary>
    public static FakeDualWriteConnector ThatTimesOut() =>
        new(new OperationCanceledException());

    /// <summary>
    /// A connector that waits for <paramref name="gate"/> and then honours the caller's token, so a test can
    /// invoke the Cancel command while the connect is in flight and drive a genuine user cancel.
    /// </summary>
    public static FakeDualWriteConnector ThatCancelsWhen(Task gate) => new(gate);

    public async Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (_gate is not null)
        {
            await _gate.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }

        if (_failWith is not null)
        {
            throw _failWith;
        }

        var gateway = new FakeCoreDualWriteGateway(_maps ?? SeedMaps(), _pollsBeforeTerminal, _failGetMapsOnCall, _emptyRequestId);
        LastGateway = gateway;
        // Stamp the environment connected to (as the real connector does), so env-gating is exercisable.
        return new DualWriteSession(gateway, "fake-cid", "Contoso (AUMF · APAC Prod)",
            env.Id, "https://fake-gateway.dual-write.example");
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

/// <summary>In-memory <see cref="IDualWriteGateway"/> for design-mode/tests. Read methods are real;
/// lifecycle actions mutate the in-memory map states (so a refresh reflects them) and the status poll
/// reports terminal success after <c>pollsBeforeTerminal</c> non-terminal polls. The remaining gateway
/// surface throws until it's needed.</summary>
public sealed class FakeCoreDualWriteGateway : IDualWriteGateway
{
    private readonly List<DualWriteMap> _maps;
    private readonly int _pollsBeforeTerminal;
    private readonly int _failGetMapsOnCall;
    private readonly bool _emptyRequestId;
    private readonly Dictionary<string, int> _pending = new();
    private int _requestSeq;
    private int _getMapsCalls;

    /// <summary>Number of <see cref="StartActionAsync"/> calls — lets tests assert no silent mutation.</summary>
    public int StartCount { get; private set; }

    /// <summary>Number of <see cref="GetMapsAsync"/> calls — lets tests assert the post-action refresh ran.</summary>
    public int GetMapsCount => _getMapsCalls;

    public FakeCoreDualWriteGateway(
        IReadOnlyList<DualWriteMap> maps,
        int pollsBeforeTerminal = 0,
        int failGetMapsOnCall = 0,
        bool emptyRequestId = false)
    {
        _maps = maps.ToList();
        _pollsBeforeTerminal = pollsBeforeTerminal;
        _failGetMapsOnCall = failGetMapsOnCall;
        _emptyRequestId = emptyRequestId;
    }

    public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default)
        => Task.FromResult(new DualWriteEnvironment("fake-cid", "Contoso (AUMF · APAC Prod)", foIdentifier));

    public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default)
    {
        // Optionally fail a specific call (e.g. the post-action refresh = call 2) to exercise the
        // refresh-failure path without clobbering an action result.
        if (++_getMapsCalls == _failGetMapsOnCall)
        {
            throw new InvalidOperationException("map refresh failed");
        }

        return Task.FromResult<IReadOnlyList<DualWriteMap>>(_maps.ToList());
    }

    public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default)
    {
        StartCount++;
        var newState = ResultState(action);
        var targetIds = maps.Select(m => m.Id).ToHashSet();
        for (var i = 0; i < _maps.Count; i++)
        {
            if (targetIds.Contains(_maps[i].Id))
            {
                _maps[i] = _maps[i] with { State = newState };
            }
        }

        // A real gateway sometimes answers a submitted action with 202 + no body (or a bare id it doesn't
        // label), leaving nothing to poll — the action still happened.
        if (_emptyRequestId)
        {
            return Task.FromResult(new DualWriteActionResponse(string.Empty, null));
        }

        var requestId = $"req-{++_requestSeq:000}";
        _pending[requestId] = _pollsBeforeTerminal;
        return Task.FromResult(new DualWriteActionResponse(requestId, "pending"));
    }

    public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default)
    {
        // Match DualWriteGatewayClient: a blank request id is a caller bug, not a pollable request. The
        // fake being lenient here hid the fact that the Operations screen was polling with an empty id.
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("A request id is required.", nameof(requestId));
        }

        if (_pending.TryGetValue(requestId, out var remaining) && remaining > 0)
        {
            _pending[requestId] = remaining - 1;
            return Task.FromResult(new DualWriteRequestStatus(requestId, "pending", IsTerminal: false, IsSuccess: false, Message: null));
        }

        return Task.FromResult(new DualWriteRequestStatus(requestId, "success", IsTerminal: true, IsSuccess: true, Message: null));
    }

    private static string ResultState(DualWriteActionType action) => action switch
    {
        DualWriteActionType.Stop => "Stopped",
        DualWriteActionType.Pause => "Paused",
        _ => "Running", // Start / Resume / InitialSync
    };

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
