using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Operations: connect + list real maps (read-path) and run lifecycle actions (confirm → submit →
/// poll to terminal → refresh). Pure VM logic over a fake connector/gateway — fast, no view, no network.
/// </summary>
public class DualWriteOpsTests
{
    private static EnvProfile Env() =>
        new("env1", "Contoso", "https://contoso.operations.dynamics.com", "tenant", "AUMF", "Tier 2", EnvStatus.Connected);

    private static EnvProfile OtherEnv() =>
        new("env2", "Fabrikam", "https://fabrikam.operations.dynamics.com", "tenant", "DEMF", "Tier 2", EnvStatus.Connected);

    private static DualWriteOpsViewModel MakeVm(IDualWriteConnector connector, bool confirm = false) =>
        new(connector, Env, new FakeDialogs(confirm),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));

    /// <summary>A mutable active-environment source: models the shell switching the active environment
    /// under this cached VM (the user having declined the "Refresh open tools?" prompt).</summary>
    private sealed class EnvSwitch
    {
        public EnvProfile? Current { get; set; } = Env();
        public EnvProfile? Get() => Current;
    }

    private sealed class FakeDialogs : IDialogService
    {
        private readonly bool _confirm;
        public FakeDialogs(bool confirm) => _confirm = confirm;
        public int Calls { get; private set; }
        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Calls++;
            return Task.FromResult(_confirm);
        }
    }

    // --- read-path ---

    [Fact]
    public async Task Load_connects_and_lists_the_real_maps()
    {
        var vm = MakeVm(new FakeDualWriteConnector());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.True(vm.HasMaps);
        Assert.Equal(FakeDualWriteConnector.SeedMaps().Count, vm.Maps.Count);
        Assert.Equal("Contoso (AUMF · APAC Prod)", vm.ConnectionName);
        Assert.Null(vm.LoadError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Map_rows_project_the_real_dualwrite_fields()
    {
        var vm = MakeVm(new FakeDualWriteConnector());

        await vm.LoadCommand.ExecuteAsync(null);

        var customers = vm.Maps.Single(m => m.Name == "Customers V3");
        Assert.Equal("account", customers.CeEntity);
        Assert.Equal("1.0.0.12", customers.Version);
        Assert.Equal("Microsoft", customers.Author);
        Assert.Equal("Running", customers.State);
    }

    [Fact]
    public async Task Load_with_no_active_environment_reports_an_error()
    {
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), () => null, new FakeDialogs(false));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.False(vm.HasMaps);
        Assert.Contains("environment", vm.LoadError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_surfaces_a_connect_failure_and_stays_disconnected()
    {
        var vm = MakeVm(FakeDualWriteConnector.ThatFails("gateway unreachable"));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("gateway unreachable", vm.LoadError);
        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Maps);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Cancelled_load_resets_to_a_clean_disconnected_state()
    {
        var vm = MakeVm(FakeDualWriteConnector.ThatCancels());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.Null(vm.ConnectionName);
        Assert.Empty(vm.Maps);
        Assert.Contains("Cancel", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Load_with_no_maps_shows_the_empty_state()
    {
        var vm = MakeVm(new FakeDualWriteConnector(Array.Empty<DualWriteMap>()));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.False(vm.HasMaps);
        Assert.Contains("No maps", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    // --- lifecycle actions ---

    [Fact]
    public async Task An_action_is_disabled_until_a_map_is_selected()
    {
        var vm = MakeVm(new FakeDualWriteConnector());
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.RunActionCommand.CanExecute(vm.StopAction)); // nothing selected

        vm.Maps.First().IsSelected = true;
        Assert.True(vm.RunActionCommand.CanExecute(vm.StopAction));
    }

    [Fact]
    public async Task Confirmed_action_submits_polls_and_refreshes_the_state()
    {
        var connector = new FakeDualWriteConnector();
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(1, connector.LastGateway!.StartCount);
        var customers = vm.Maps.Single(m => m.Name == "Customers V3");
        Assert.Equal("Stopped", customers.State);   // refreshed to the new state
        Assert.True(customers.IsSelected);            // selection preserved across the refresh
        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Cancelled_confirm_does_not_submit_anything()
    {
        var connector = new FakeDualWriteConnector();
        var vm = MakeVm(connector, confirm: false);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(0, connector.LastGateway!.StartCount);                  // no gateway mutation
        Assert.Equal("Running", vm.Maps.Single(m => m.Name == "Customers V3").State); // unchanged
    }

    [Fact]
    public async Task A_refresh_failure_does_not_clobber_a_successful_action()
    {
        // Load = GetMaps call 1 (ok); the post-action refresh = call 2 (throws).
        var connector = new FakeDualWriteConnector(failGetMapsOnCall: 2);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase); // action result stands
        Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_action_polls_until_the_request_is_terminal()
    {
        var connector = new FakeDualWriteConnector(pollsBeforeTerminal: 3);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StartAction);

        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    // --- #152: the session is pinned to the environment it was connected for ---

    [Fact]
    public async Task An_action_is_refused_after_the_active_environment_changes()
    {
        var connector = new FakeDualWriteConnector();
        var dialogs = new FakeDialogs(confirm: true);
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(connector, env.Get, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        env.Current = OtherEnv();   // shell switched; the cached session still belongs to env1

        Assert.False(vm.RunActionCommand.CanExecute(vm.StopAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);   // hard guard, not just CanExecute

        Assert.Equal(0, connector.LastGateway!.StartCount);      // no gateway call at all
        Assert.Equal(0, dialogs.Calls);                          // refused before the confirm dialog
        Assert.Equal("Running", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.Contains("reconnect", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("Contoso") && e.Text.Contains("Fabrikam"));
    }

    [Fact]
    public async Task An_action_still_runs_while_the_active_environment_is_unchanged()
    {
        var connector = new FakeDualWriteConnector();
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(connector, env.Get, new FakeDialogs(confirm: true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        Assert.True(vm.RunActionCommand.CanExecute(vm.StopAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reconnecting_after_the_switch_restores_the_actions()
    {
        var connector = new FakeDualWriteConnector();
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(connector, env.Get, new FakeDialogs(confirm: true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        env.Current = OtherEnv();

        await vm.LoadCommand.ExecuteAsync(null);   // explicit reconnect (never automatic)
        vm.Maps.First().IsSelected = true;

        Assert.True(vm.RunActionCommand.CanExecute(vm.StopAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        Assert.Equal(1, connector.LastGateway!.StartCount);
    }

    // --- #53: debug-mode toggle ---

    private sealed class ScriptedODataClient : IODataClient
    {
        private readonly string _getBody;
        public List<(string Method, string Path, string? Body)> Calls { get; } = new();
        public ScriptedODataClient(string getBody) => _getBody = getBody;

        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
            => SendAsync(method, path, body, null, ct);

        public Task<ODataResponse> SendAsync(string method, string path, string? body,
            IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
        {
            Calls.Add((method, path, body));
            var resp = method == "GET"
                ? new ODataResponse(200, "OK", _getBody, 1)
                : new ODataResponse(204, "No Content", string.Empty, 1);
            return Task.FromResult(resp);
        }
    }

    private sealed class FixedMetadata : IMetadataService
    {
        private readonly IReadOnlyList<EntitySet> _entities;
        public FixedMetadata(params string[] names) =>
            _entities = names.Select(n => new EntitySet(n, "DualWrite", 5, "Id", false, string.Empty)).ToList();
        public IReadOnlyList<EntitySet> GetEntities() => _entities;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static DualWriteOpsViewModel MakeDebugVm(IODataClient odata, IMetadataService metadata) =>
        new(new FakeDualWriteConnector(), Env, new FakeDialogs(false),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: metadata);

    [Fact]
    public void Debug_toggle_is_disabled_until_connected_with_a_selection()
    {
        var vm = MakeDebugVm(new ScriptedODataClient("{}"), new FixedMetadata("DualWriteProjectConfigurations"));

        Assert.False(vm.EnableDebugForSelectedCommand.CanExecute(null));
        Assert.False(vm.DisableDebugForSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Enabling_debug_patches_IsDebugMode_on_the_selected_maps_project_config()
    {
        const string record = "{\"value\":[{\"@odata.id\":\"https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)\",\"IsDebugMode\":\"No\"}]}";
        var odata = new ScriptedODataClient(record);
        var vm = MakeDebugVm(odata, new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        var patch = odata.Calls.Single(c => c.Method == "PATCH");
        Assert.Equal("https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)", patch.Path);
        Assert.Equal(DualWriteDebugMode.BuildPatchBody(true), patch.Body);
        Assert.Contains("enabled", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Debug_toggle_explains_when_the_config_entity_is_absent_from_metadata()
    {
        var vm = MakeDebugVm(new ScriptedODataClient("{}"), new FixedMetadata("SomeOtherEntity"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        Assert.Contains("DualWriteProjectConfiguration", vm.DebugStatus);
    }

    [Fact]
    public async Task Debug_toggle_is_refused_after_the_active_environment_changes()
    {
        // The project ids come from the old session's maps but _odata resolves the active environment per
        // call — the one operation would otherwise straddle two environments.
        const string record = "{\"value\":[{\"@odata.id\":\"https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)\",\"IsDebugMode\":\"No\"}]}";
        var odata = new ScriptedODataClient(record);
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, new FakeDialogs(false),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        env.Current = OtherEnv();

        Assert.False(vm.EnableDebugForSelectedCommand.CanExecute(null));
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);   // hard guard, not just CanExecute

        Assert.Empty(odata.Calls);                                   // no GET, no PATCH
        Assert.Contains("reconnect", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconnect", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("Contoso") && e.Text.Contains("Fabrikam"));
    }

    // --- gateway log (self-diagnosis: which host, which cid, what happened) ---

    [Fact]
    public async Task Successful_load_logs_the_gateway_host_the_cid_and_the_map_count()
    {
        var vm = MakeVm(new FakeDualWriteConnector());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasGatewayLog);
        Assert.Contains(vm.GatewayLog, e => e.Text.Contains("fake-gateway.dual-write.example"));
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Ok && e.Text.Contains("fake-cid"));
        Assert.Contains(vm.GatewayLog, e => e.Text.Contains("Loaded") && e.Text.Contains("map"));
    }

    [Fact]
    public async Task A_connect_failure_is_logged_as_an_error_line()
    {
        var vm = MakeVm(FakeDualWriteConnector.ThatFails("no connection (cid) for this environment"));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Err && e.Text.Contains("no connection"));
    }

    [Fact]
    public async Task Reconnecting_resets_the_log_to_only_the_current_attempt()
    {
        // A failed first attempt should not interleave with a later one — the log isolates the latest connect.
        var vm = MakeVm(FakeDualWriteConnector.ThatFails("first attempt failed"));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains(vm.GatewayLog, e => e.Text.Contains("first attempt failed"));

        // Re-point at a working connector and reconnect (the VM connects fresh each Load).
        var vm2 = MakeVm(new FakeDualWriteConnector());
        await vm2.LoadCommand.ExecuteAsync(null);
        await vm2.LoadCommand.ExecuteAsync(null); // second connect on the same VM

        // Exactly one "Connecting…" line — the prior attempt's entries were cleared, not appended to.
        Assert.Equal(1, vm2.GatewayLog.Count(e => e.Text.StartsWith("Connecting")));
    }
}
