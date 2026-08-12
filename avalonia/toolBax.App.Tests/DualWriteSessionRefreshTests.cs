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
/// #166: the delegated Data Integrator token lasts about an hour, and the sign-in hands back a refresh
/// token alongside it. Both entry points built their gateway with the <em>static</em> bearer overload, so
/// every operation past that hour failed with a bare 401 until the user signed in again — while
/// <see cref="IDualWriteGatewayFactory.CreateRefreshing"/> sat unused. These pin the wiring, not the
/// renewal itself (that lives in <c>RefreshingBearerTokenHandlerTests</c>).
/// </summary>
public class DualWriteSessionRefreshTests
{
    private static EnvProfile Env() =>
        new("env1", "PGW", "https://pgw.operations.dynamics.com", "tenant", "USMF", "Tier 2", EnvStatus.Connected);

    private static FakeDualWriteSignIn SignIn(string? refreshToken) =>
        new(new DualWriteSignInResult(
            new DualWriteToken("access", refreshToken, DateTimeOffset.UtcNow.AddHours(1)),
            "https://gw.example"));

    /// <summary>Records which factory overload the caller reached for, and the callback it supplied.</summary>
    private sealed class RecordingFactory : IDualWriteGatewayFactory
    {
        private readonly IDualWriteGateway _gateway;
        public RecordingFactory(IDualWriteGateway gateway) => _gateway = gateway;

        public int CreateCalls { get; private set; }
        public int CreateRefreshingCalls { get; private set; }
        public Func<DualWriteToken, Task>? OnRefreshed { get; private set; }
        public DualWriteConnectionSettings? Settings { get; private set; }

        public IDualWriteGateway Create(DualWriteConnectionSettings settings)
        {
            CreateCalls++;
            Settings = settings;
            return _gateway;
        }

        public IDualWriteGateway CreateRefreshing(DualWriteConnectionSettings settings, Func<DualWriteToken, Task> onRefreshed)
        {
            CreateRefreshingCalls++;
            Settings = settings;
            OnRefreshed = onRefreshed;
            return _gateway;
        }
    }

    private sealed class StubGateway : IDualWriteGateway, IDisposable
    {
        public void Dispose()
        {
        }

        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult(new DualWriteEnvironment("real-cid", "Contoso", foIdentifier));

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

    [Fact]
    public async Task ConnectAsync_uses_the_refreshing_client_when_the_sign_in_yielded_a_refresh_token()
    {
        var factory = new RecordingFactory(new StubGateway());
        var connector = new CoreDualWriteConnector(SignIn("refresh-1"), factory);

        await connector.ConnectAsync(Env(), TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.CreateRefreshingCalls);
        Assert.Equal(0, factory.CreateCalls);
        Assert.NotNull(factory.OnRefreshed);
        Assert.True(factory.Settings!.HasDelegatedSession);
        // The callback must be safe to invoke: nothing persists tokens today, so it only traces.
        await factory.OnRefreshed!(new DualWriteToken("next", "refresh-2", DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task ConnectAsync_falls_back_to_the_static_bearer_when_there_is_no_refresh_token()
    {
        var factory = new RecordingFactory(new StubGateway());
        var connector = new CoreDualWriteConnector(SignIn(refreshToken: null), factory);

        await connector.ConnectAsync(Env(), TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(0, factory.CreateRefreshingCalls);
    }

    [Fact]
    public async Task TestAsync_uses_the_refreshing_client_when_the_sign_in_yielded_a_refresh_token()
    {
        var factory = new RecordingFactory(new StubGateway());
        var tester = new CoreDualWriteGatewayTester(SignIn("refresh-1"), factory);

        var result = await tester.TestAsync(Env(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, factory.CreateRefreshingCalls);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public async Task TestAsync_falls_back_to_the_static_bearer_when_there_is_no_refresh_token()
    {
        var factory = new RecordingFactory(new StubGateway());
        var tester = new CoreDualWriteGatewayTester(SignIn(refreshToken: null), factory);

        await tester.TestAsync(Env(), TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(0, factory.CreateRefreshingCalls);
    }
}
