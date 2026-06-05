using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DualWriteComparePlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteCompareViewModelTests
{
    private sealed class PassthroughProtector : ITokenProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string? Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class StubGateway : IDualWriteGateway
    {
        private readonly string _cid;
        private readonly List<DualWriteMap> _maps;

        public StubGateway(string cid, List<DualWriteMap> maps)
        {
            _cid = cid;
            _maps = maps;
        }

        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken ct = default) =>
            Task.FromResult(new DualWriteEnvironment(_cid, _cid, foIdentifier));

        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DualWriteMap>>(_maps);

        public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId, string templateId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RefreshTablesAsync(string fieldMappingName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities, bool forceReset, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName, IReadOnlyList<string> keyFields, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class KeyedFactory : IDualWriteGatewayFactory
    {
        private readonly Dictionary<string, IDualWriteGateway> _byKey;
        public KeyedFactory(Dictionary<string, IDualWriteGateway> byKey) => _byKey = byKey;
        public IDualWriteGateway Create(DualWriteConnectionSettings settings) => _byKey[settings.Key];
        public IDualWriteGateway CreateRefreshing(DualWriteConnectionSettings settings, Func<DualWriteToken, Task> onRefreshed) => _byKey[settings.Key];
    }

    private sealed class FakeContext : IPluginContext
    {
        public FoEnvironment CurrentEnv { get; set; } =
            new("env-1", "UAT", "https://uat.operations.dynamics.com", "tenant-1", null);
        public IODataClient OData => null!;
        public ICatalogService Catalog => null!;
        public Microsoft.Extensions.Logging.ILogger Logger => NullLogger.Instance;
    }

    private static DualWriteMap Map(string name, string version, string state) =>
        new($"id-{name}", name, name, $"pid-{name}", state,
            new DualWriteTemplate($"t-{version}", version, "MS"), Array.Empty<DualWriteTemplate>());

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Editor_SignIn_StoresDiscoveredGatewayAndDelegatedSession()
    {
        // #24: each side authenticates via interactive sign-in (no pasted token); the resolved
        // gateway URL + delegated session are stored, keyed independently per side.
        var path = Path.Combine(Path.GetTempPath(), $"dwcmp-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            var editor = new ConnectionEditorViewModel("Left", "Environment A", store,
                (_, _) => Task.FromResult<DualWriteSignInResult?>(new DualWriteSignInResult(
                    new DualWriteToken("acc", "ref", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    "https://gw.discovered")),
                _ => { })
            {
                FoIdentifier = "uat-fo"
            };

            await editor.SignInCommand.ExecuteAsync();

            var saved = await store.GetAsync("Left", CancellationToken.None);
            Assert.Equal("https://gw.discovered", saved.GatewayBaseUrl);
            Assert.Equal("acc", saved.BearerToken);
            Assert.Equal("ref", saved.RefreshToken);
            Assert.True(saved.HasDelegatedSession);
            Assert.Equal("https://gw.discovered", editor.GatewayBaseUrl);
        }
        finally { File.Delete(path); }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Editor_SignInCancelled_DoesNotStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwcmp-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            var editor = new ConnectionEditorViewModel("Left", "Environment A", store,
                (_, _) => Task.FromResult<DualWriteSignInResult?>(null), _ => { })
            {
                FoIdentifier = "uat-fo"
            };

            await editor.SignInCommand.ExecuteAsync();

            var saved = await store.GetAsync("Left", CancellationToken.None);
            Assert.False(saved.IsComplete);
            Assert.Contains("cancelled", editor.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Editor_ClearToken_RemovesSession_KeepsGatewayAndIdentifier()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwcmp-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            await store.SaveAsync(new DualWriteConnectionSettings("Right", "https://gw", "uat-fo", "tok"), CancellationToken.None);
            var editor = new ConnectionEditorViewModel("Right", "Environment B", store,
                (_, _) => Task.FromResult<DualWriteSignInResult?>(null), _ => { });

            await editor.ClearTokenCommand.ExecuteAsync();

            var saved = await store.GetAsync("Right", CancellationToken.None);
            Assert.True(string.IsNullOrEmpty(saved.BearerToken));
            Assert.Equal("https://gw", saved.GatewayBaseUrl);
            Assert.Equal("uat-fo", saved.FoIdentifier);
        }
        finally { File.Delete(path); }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Editor_SignIn_IsIndependentPerSide()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwcmp-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            DualWriteSignInResult? Flow(string id, bool clear) =>
                new DualWriteSignInResult(new DualWriteToken($"acc-{id}", "ref", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), $"https://gw-{id}");

            var left = new ConnectionEditorViewModel("Left", "A", store, (id, c) => Task.FromResult(Flow(id, c)), _ => { }) { FoIdentifier = "left-fo" };
            var right = new ConnectionEditorViewModel("Right", "B", store, (id, c) => Task.FromResult(Flow(id, c)), _ => { }) { FoIdentifier = "right-fo" };

            await left.SignInCommand.ExecuteAsync();
            await right.SignInCommand.ExecuteAsync();

            Assert.Equal("acc-left-fo", (await store.GetAsync("Left", CancellationToken.None)).BearerToken);
            Assert.Equal("acc-right-fo", (await store.GetAsync("Right", CancellationToken.None)).BearerToken);
        }
        finally { File.Delete(path); }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Compare_LoadsBothEnvironments_AndProducesDiffRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwcmp-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            await store.SaveAsync(new DualWriteConnectionSettings("Left", "https://a", "id-a", "tok-a"), CancellationToken.None);
            await store.SaveAsync(new DualWriteConnectionSettings("Right", "https://b", "id-b", "tok-b"), CancellationToken.None);

            var factory = new KeyedFactory(new Dictionary<string, IDualWriteGateway>
            {
                ["Left"] = new StubGateway("cidA", new List<DualWriteMap> { Map("Customers", "1.0", "Running"), Map("Vendors", "1.0", "Running") }),
                ["Right"] = new StubGateway("cidB", new List<DualWriteMap> { Map("Customers", "2.0", "Running") })
            });

            var vm = new DualWriteCompareViewModel(new FakeContext(), store, factory);

            await vm.CompareCommand.ExecuteAsync();

            Assert.Equal(2, vm.Rows.Count);
            Assert.Equal(DualWriteComparisonVerdict.VersionMismatch, vm.Rows.Single(r => r.MapName == "Customers").Verdict);
            Assert.Equal(DualWriteComparisonVerdict.OnlyInLeft, vm.Rows.Single(r => r.MapName == "Vendors").Verdict);
            Assert.Contains("difference", vm.SummaryMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Compare_IncompleteConnection_DoesNotLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwcmp-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            await store.SaveAsync(new DualWriteConnectionSettings("Left", "https://a", "id-a", "tok-a"), CancellationToken.None);
            // Right intentionally not configured.

            var factory = new KeyedFactory(new Dictionary<string, IDualWriteGateway>());
            var vm = new DualWriteCompareViewModel(new FakeContext(), store, factory);

            await vm.CompareCommand.ExecuteAsync();

            Assert.Empty(vm.Rows);
            Assert.Contains("need a gateway URL", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
