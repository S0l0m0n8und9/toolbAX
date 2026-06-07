using System;
using System.Linq;
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

    private static DualWriteOpsViewModel MakeVm(IDualWriteConnector connector, bool confirm = false) =>
        new(connector, Env, new FakeDialogs(confirm),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));

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
}
