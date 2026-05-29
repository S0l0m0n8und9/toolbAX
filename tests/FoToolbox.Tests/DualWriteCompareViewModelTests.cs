using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DualWriteComparePlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.DualWrite;
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
    }

    private sealed class KeyedFactory : IDualWriteGatewayFactory
    {
        private readonly Dictionary<string, IDualWriteGateway> _byKey;
        public KeyedFactory(Dictionary<string, IDualWriteGateway> byKey) => _byKey = byKey;
        public IDualWriteGateway Create(DualWriteConnectionSettings settings) => _byKey[settings.Key];
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
