using System;
using System.Linq;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Operations read-path: connect to the gateway for the active environment and list its real maps.
/// Pure VM logic over a fake connector — fast, no view, no live gateway.
/// </summary>
public class DualWriteOpsTests
{
    private static EnvProfile Env() =>
        new("env1", "Contoso", "https://contoso.operations.dynamics.com", "tenant", "AUMF", "Tier 2", EnvStatus.Connected);

    [Fact]
    public async Task Load_connects_and_lists_the_real_maps()
    {
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), Env);

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
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), Env);

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
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), () => null);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.False(vm.HasMaps);
        Assert.Contains("environment", vm.LoadError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_surfaces_a_connect_failure_and_stays_disconnected()
    {
        var vm = new DualWriteOpsViewModel(FakeDualWriteConnector.ThatFails("gateway unreachable"), Env);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("gateway unreachable", vm.LoadError);
        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Maps);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Load_with_no_maps_shows_the_empty_state()
    {
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(Array.Empty<DualWriteMap>()), Env);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.False(vm.HasMaps);
        Assert.Contains("No maps", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
}
