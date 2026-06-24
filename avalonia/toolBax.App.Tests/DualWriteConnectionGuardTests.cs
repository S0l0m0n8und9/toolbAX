using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// The empty-cid guard (#cid-error fix): the gateway returns an empty cid (no exception) when an F&O
/// environment isn't in a dual-write connection set, which previously surfaced as a cryptic
/// "A connection id (cid) is required." later, and as a false "Linked" in the gateway tester.
/// </summary>
public class DualWriteConnectionGuardTests
{
    private static EnvProfile Env() =>
        new("env1", "PGW", "https://pgw.operations.dynamics.com", "tenant", "USMF", "Tier 2", EnvStatus.Connected);

    // Minimal IDualWriteGateway whose environment lookup returns a configurable cid; tracks disposal.
    private sealed class StubGateway : IDualWriteGateway, IDisposable
    {
        private readonly DualWriteEnvironment _env;
        public bool Disposed { get; private set; }
        public StubGateway(string cid, string cname = "Contoso") => _env = new DualWriteEnvironment(cid, cname, "id");

        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult(_env);
        public void Dispose() => Disposed = true;

        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId, string templateId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RefreshTablesAsync(string fieldMappingName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities, bool forceReset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName, IReadOnlyList<string> keyFields, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubFactory : IDualWriteGatewayFactory
    {
        private readonly IDualWriteGateway _gateway;
        public StubFactory(IDualWriteGateway gateway) => _gateway = gateway;
        public IDualWriteGateway Create(DualWriteConnectionSettings settings) => _gateway;
        public IDualWriteGateway CreateRefreshing(DualWriteConnectionSettings settings, Func<DualWriteToken, Task> onRefreshed) => _gateway;
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("real-cid", true)]
    public void IsLinked_requires_a_non_blank_cid(string cid, bool expected)
        => Assert.Equal(expected, DualWriteConnectionGuard.IsLinked(new DualWriteEnvironment(cid, "n", "id")));

    [Fact]
    public async Task ConnectAsync_throws_an_actionable_error_when_the_gateway_returns_no_cid()
    {
        var gateway = new StubGateway(cid: string.Empty);
        var connector = new CoreDualWriteConnector(new FakeDualWriteSignIn(), new StubFactory(gateway));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.ConnectAsync(Env(), TestContext.Current.CancellationToken));

        Assert.Contains("No dual-write connection", ex.Message);
        Assert.Contains("pgw.operations.dynamics.com", ex.Message);
        Assert.DoesNotContain("cid is required", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(gateway.Disposed); // the orphaned gateway's HttpClient is disposed on the failure path
    }

    [Fact]
    public async Task ConnectAsync_returns_a_session_when_a_cid_is_present()
    {
        var connector = new CoreDualWriteConnector(new FakeDualWriteSignIn(), new StubFactory(new StubGateway("real-cid", "Contoso")));

        var session = await connector.ConnectAsync(Env(), TestContext.Current.CancellationToken);

        Assert.Equal("real-cid", session.Cid);
        Assert.Equal("Contoso", session.Cname);
    }

    [Fact]
    public async Task TestAsync_reports_failure_not_a_false_linked_when_the_cid_is_empty()
    {
        var tester = new CoreDualWriteGatewayTester(new FakeDualWriteSignIn(), new StubFactory(new StubGateway(string.Empty)));

        var result = await tester.TestAsync(Env(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("No dual-write connection", result.Message);
        Assert.DoesNotContain("Linked:", result.Message);
    }

    [Fact]
    public async Task TestAsync_reports_linked_with_the_cid_when_present()
    {
        var tester = new CoreDualWriteGatewayTester(new FakeDualWriteSignIn(), new StubFactory(new StubGateway("real-cid", "Contoso")));

        var result = await tester.TestAsync(Env(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("real-cid", result.Message);
        Assert.Contains("Contoso", result.Message);
    }
}
