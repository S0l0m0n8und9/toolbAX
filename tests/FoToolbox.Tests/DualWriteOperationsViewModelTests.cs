using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DualWriteOperationsPlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteOperationsViewModelTests
{
    private sealed class PassthroughProtector : ITokenProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string? Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class FakeGateway : IDualWriteGateway
    {
        public DualWriteEnvironment Environment { get; set; } = new("C1", "contoso-link", "id");
        public List<DualWriteMap> MapsList { get; } = new();
        public List<(DualWriteActionType Action, int MapCount, string Cid)> Actions { get; } = new();
        public DualWriteActionResponse ActionResponse { get; set; } = new("req-1", "1");
        public Queue<DualWriteRequestStatus> StatusQueue { get; } = new();
        public int GetMapsCalls { get; private set; }

        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken ct = default) =>
            Task.FromResult(Environment);

        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken ct = default)
        {
            GetMapsCalls++;
            return Task.FromResult<IReadOnlyList<DualWriteMap>>(MapsList.ToList());
        }

        public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken ct = default)
        {
            Actions.Add((action, maps.Count, cid));
            return Task.FromResult(ActionResponse);
        }

        public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken ct = default) =>
            Task.FromResult(StatusQueue.Count > 0
                ? StatusQueue.Dequeue()
                : new DualWriteRequestStatus(requestId, "2", true, true, null));

        public List<(string Cid, string Pid, string TemplateId)> Switches { get; } = new();

        public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId, string templateId, CancellationToken ct = default)
        {
            Switches.Add((cid, projectId, templateId));
            return Task.FromResult(new DualWriteActionResponse(string.Empty, null));
        }

        public Dictionary<string, List<DualWriteFieldMapping>> FieldMappingsByPid { get; } = new();
        public List<string> Refreshed { get; } = new();

        public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DualWriteFieldMapping>>(
                FieldMappingsByPid.TryGetValue(projectId, out var list) ? list : new List<DualWriteFieldMapping>());

        public Task RefreshTablesAsync(string fieldMappingName, CancellationToken ct = default)
        {
            Refreshed.Add(fieldMappingName);
            return Task.CompletedTask;
        }

        public DualWriteConnectionSet ConnectionSet { get; set; } =
            new("connset", Array.Empty<DualWriteConnectionSetEnvironment>(), Array.Empty<string>());
        public List<(string Cid, IReadOnlyList<string> LegalEntities, bool ForceReset)> Resets { get; } = new();

        public Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname, CancellationToken ct = default) =>
            Task.FromResult(ConnectionSet);

        public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities, bool forceReset, CancellationToken ct = default)
        {
            Resets.Add((cid, legalEntities, forceReset));
            return Task.CompletedTask;
        }

        public List<(string Dataset, string Entity, IReadOnlyList<string> Fields)> KeyApplications { get; } = new();

        public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName, IReadOnlyList<string> keyFields, CancellationToken ct = default)
        {
            KeyApplications.Add((datasetName, ceEntityName, keyFields));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFactory : IDualWriteGatewayFactory
    {
        private readonly IDualWriteGateway _gateway;
        public FakeFactory(IDualWriteGateway gateway) => _gateway = gateway;
        public IDualWriteGateway Create(DualWriteConnectionSettings settings) => _gateway;
        public IDualWriteGateway CreateRefreshing(DualWriteConnectionSettings settings, Func<DualWriteToken, Task> onRefreshed) => _gateway;
        public IDualWriteGateway CreateWithTokenProvider(string gatewayBaseUrl, Func<CancellationToken, Task<string>> getToken, HttpMessageHandler? innerHandler = null) => throw new NotImplementedException();
    }

    private sealed class FakeContext : IPluginContext
    {
        public FoEnvironment CurrentEnv { get; set; } =
            new("env-1", "UAT", "https://uat.operations.dynamics.com", "tenant-1", null);
        public IODataClient OData => null!;
        public ICatalogService Catalog => null!;
        public Microsoft.Extensions.Logging.ILogger Logger => NullLogger.Instance;
    }

    private static DualWriteMap Map(string id, string name = "Map") =>
        new(id, name, name, $"pid-{id}", "Stopped", new DualWriteTemplate($"t-{id}", "1.0", "MS"), Array.Empty<DualWriteTemplate>());

    private static async Task<DualWriteConnectionStore> SeededStoreAsync(string path)
    {
        var store = new DualWriteConnectionStore(path, new PassthroughProtector());
        await store.SaveAsync(new DualWriteConnectionSettings("env-1", "https://gw.example", "uat-fo", "tok-123"), CancellationToken.None);
        return store;
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SignIn_StoresDiscoveredGatewayAndDelegatedToken()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            var gateway = new FakeGateway();
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                FoIdentifier = "uat-fo",
                SignInFlow = (_, _) => Task.FromResult<DualWriteSignInResult?>(new DualWriteSignInResult(
                    new DualWriteToken("acc", "ref", new DateTimeOffset(2026, 5, 29, 1, 0, 0, TimeSpan.Zero)),
                    "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com"))
            };

            await vm.SignInCommand.ExecuteAsync();

            Assert.Equal("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com", vm.GatewayBaseUrl);
            var saved = await store.GetAsync("env-1", CancellationToken.None);
            Assert.Equal("acc", saved.BearerToken);
            Assert.Equal("ref", saved.RefreshToken);
            Assert.True(saved.HasDelegatedSession);
            Assert.True(saved.IsComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SignIn_Cancelled_DoesNotSave()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(new FakeGateway()))
            {
                FoIdentifier = "uat-fo",
                SignInFlow = (_, _) => Task.FromResult<DualWriteSignInResult?>(null)
            };

            await vm.SignInCommand.ExecuteAsync();

            var saved = await store.GetAsync("env-1", CancellationToken.None);
            Assert.False(saved.IsComplete);
            Assert.Contains("cancelled", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task LoadMaps_ResolvesEnvironmentAndPopulatesMaps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a", "Customers"));
            gateway.MapsList.Add(Map("b", "Vendors"));
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway));

            await vm.LoadMapsCommand.ExecuteAsync();

            Assert.Equal(2, vm.Maps.Count);
            Assert.Contains(vm.Maps, m => m.Name == "Customers");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task StartAction_WithoutSelection_DoesNotCallGateway()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a"));
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ConfirmAction = (_, _) => true
            };
            await vm.LoadMapsCommand.ExecuteAsync();

            await vm.StartCommand.ExecuteAsync();

            Assert.Empty(gateway.Actions);
            Assert.Contains("Select at least one map", vm.StatusMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task StartAction_Confirmed_SubmitsActionAndPollsToCompletion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a"));
            gateway.StatusQueue.Enqueue(new DualWriteRequestStatus("req-1", "running", false, false, null));
            gateway.StatusQueue.Enqueue(new DualWriteRequestStatus("req-1", "2", true, true, null));
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ConfirmAction = (_, _) => true,
                PollInterval = TimeSpan.Zero
            };
            await vm.LoadMapsCommand.ExecuteAsync();
            vm.Maps[0].IsSelected = true;

            await vm.StartCommand.ExecuteAsync();

            var action = Assert.Single(gateway.Actions);
            Assert.Equal(DualWriteActionType.Start, action.Action);
            Assert.Equal(1, action.MapCount);
            Assert.Equal("C1", action.Cid);
            Assert.Contains("completed", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task ApplyLatestVersion_PicksHighestTemplate_AndCallsSwitchActive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            var mapWithTemplates = new DualWriteMap(
                "a", "Customers", "Customers", "pid-a", "Running",
                new DualWriteTemplate("t-old", "1.0.0", "MS"),
                new[]
                {
                    new DualWriteTemplate("t-old", "1.0.0", "MS"),
                    new DualWriteTemplate("t-new", "1.2.0", "MS")
                });
            gateway.MapsList.Add(mapWithTemplates);
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ConfirmAction = (_, _) => true
            };
            await vm.LoadMapsCommand.ExecuteAsync();
            vm.Maps[0].IsSelected = true;

            await vm.ApplyLatestVersionCommand.ExecuteAsync();

            var sw = Assert.Single(gateway.Switches);
            Assert.Equal("C1", sw.Cid);
            Assert.Equal("pid-a", sw.Pid);
            Assert.Equal("t-new", sw.TemplateId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task RefreshTables_RefreshesEachFieldMappingOfSelectedMaps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a"));
            gateway.FieldMappingsByPid["pid-a"] = new List<DualWriteFieldMapping>
            {
                new("fm-1"),
                new("fm-2")
            };
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ConfirmAction = (_, _) => true
            };
            await vm.LoadMapsCommand.ExecuteAsync();
            vm.Maps[0].IsSelected = true;

            await vm.RefreshTablesCommand.ExecuteAsync();

            Assert.Equal(new[] { "fm-1", "fm-2" }, gateway.Refreshed.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task ExportConfig_WritesLoadedMapsToChosenPath()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        var exportPath = Path.Combine(Path.GetTempPath(), $"dwexport-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(storePath);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a", "Customers"));
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ChooseExportPath = _ => exportPath
            };
            await vm.LoadMapsCommand.ExecuteAsync();

            await vm.ExportConfigCommand.ExecuteAsync();

            Assert.True(File.Exists(exportPath));
            var json = await File.ReadAllTextAsync(exportPath);
            Assert.Contains("Customers", json);
            Assert.Contains("\"cid\": \"C1\"", json);
        }
        finally
        {
            File.Delete(storePath);
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task ResetLink_Confirmed_ResetsConnectionSetLegalEntities()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a"));
            gateway.ConnectionSet = new DualWriteConnectionSet(
                "cs",
                new[] { new DualWriteConnectionSetEnvironment("ce", "CE", "pae", false, "CRM", "https://ce", Array.Empty<DualWriteSchema>()) },
                new[] { "USMF", "DEMF" });
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ConfirmAction = (_, _) => true,
                ForceReset = true
            };
            await vm.LoadMapsCommand.ExecuteAsync();

            await vm.ResetLinkCommand.ExecuteAsync();

            var reset = Assert.Single(gateway.Resets);
            Assert.Equal("C1", reset.Cid);
            Assert.True(reset.ForceReset);
            Assert.Equal(new[] { "USMF", "DEMF" }, reset.LegalEntities.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task ApplyIntegrationKeys_ResolvesKeyFromConnectionSet_AndPostsForResolvedMaps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a", "Customers") with { RightEntityName = "Customers" });
            gateway.MapsList.Add(Map("b", "Vendors") with { RightEntityName = "Unmapped" });
            gateway.ConnectionSet = new DualWriteConnectionSet(
                "cs",
                new[]
                {
                    new DualWriteConnectionSetEnvironment("ce-prod", "CE", "pae", false, "CRM", "https://ce",
                        new[] { new DualWriteSchema("Customers", new[] { new DualWriteSchemaKey("USERKEYS", "k", new[] { "accountnumber" }) }) })
                },
                Array.Empty<string>());
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ConfirmAction = (_, _) => true
            };
            await vm.LoadMapsCommand.ExecuteAsync();
            foreach (var row in vm.Maps) row.IsSelected = true;

            await vm.ApplyIntegrationKeysCommand.ExecuteAsync();

            var applied = Assert.Single(gateway.KeyApplications);
            Assert.Equal("ce-prod", applied.Dataset);
            Assert.Equal("Customers", applied.Entity);
            Assert.Equal(new[] { "accountnumber" }, applied.Fields.ToArray());
            Assert.Contains("Skipped 1", vm.StatusMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Action_Declined_DoesNotSubmit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = await SeededStoreAsync(path);
            var gateway = new FakeGateway();
            gateway.MapsList.Add(Map("a"));
            var vm = new DualWriteOperationsViewModel(new FakeContext(), store, new FakeFactory(gateway))
            {
                ConfirmAction = (_, _) => false
            };
            await vm.LoadMapsCommand.ExecuteAsync();
            vm.Maps[0].IsSelected = true;

            await vm.StopCommand.ExecuteAsync();

            Assert.Empty(gateway.Actions);
            Assert.Contains("cancelled", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class DualWriteConnectionStoreTests
{
    private sealed class PassthroughProtector : ITokenProtector
    {
        public string Protect(string plaintext) => "P:" + plaintext;
        public string? Unprotect(string protectedValue) => protectedValue.StartsWith("P:") ? protectedValue[2..] : protectedValue;
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SaveThenGet_RoundTripsAllFields_AndDecryptsToken()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            await store.SaveAsync(new DualWriteConnectionSettings("env-1", "https://gw", "uat-fo", "secret"), CancellationToken.None);

            var reloaded = new DualWriteConnectionStore(path, new PassthroughProtector());
            var settings = await reloaded.GetAsync("env-1", CancellationToken.None);

            Assert.Equal("https://gw", settings.GatewayBaseUrl);
            Assert.Equal("uat-fo", settings.FoIdentifier);
            Assert.Equal("secret", settings.BearerToken);
            Assert.True(settings.IsComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task TokenIsNotStoredInPlaintext()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            await store.SaveAsync(new DualWriteConnectionSettings("env-1", "https://gw", "uat-fo", "secret"), CancellationToken.None);

            var raw = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("\"secret\"", raw);
            Assert.Contains("P:secret", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task Connections_AreIsolatedPerEnvironment()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dwc-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DualWriteConnectionStore(path, new PassthroughProtector());
            await store.SaveAsync(new DualWriteConnectionSettings("env-a", "https://a", "id-a", "tok-a"), CancellationToken.None);
            await store.SaveAsync(new DualWriteConnectionSettings("env-b", "https://b", "id-b", "tok-b"), CancellationToken.None);

            var a = await store.GetAsync("env-a", CancellationToken.None);
            var b = await store.GetAsync("env-b", CancellationToken.None);

            Assert.Equal("https://a", a.GatewayBaseUrl);
            Assert.Equal("tok-b", b.BearerToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task DpapiProtector_RoundTrips()
    {
        var protector = new DpapiTokenProtector();
        var protectedValue = protector.Protect("super-secret-token");
        Assert.NotEqual("super-secret-token", protectedValue);
        Assert.Equal("super-secret-token", protector.Unprotect(protectedValue));
        await Task.CompletedTask;
    }
}
