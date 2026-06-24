using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class VirtualTablesViewModelTests
{
    private static EnvProfile EnvWith(string? dataverseUrl) =>
        new("env1", "Env", "contoso.operations.dynamics.com", "tenant", "USMF", "Tier 1",
            EnvStatus.Connected, DataverseUrl: dataverseUrl);

    private sealed class ErrorReader : IVirtualTableReader
    {
        public Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
            => Task.FromResult(VirtualTableLoadResult.Fail("boom"));
    }

    [Fact]
    public async Task Initialize_lists_only_fo_backed_tables_and_counts_the_rest()
    {
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Tables.Count);
        Assert.All(vm.Tables, t => Assert.True(t.IsFinanceAndOperations));
        Assert.Equal(1, vm.OtherVirtualCount);
        Assert.True(vm.HasOtherVirtual);
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public async Task Selecting_a_table_with_a_dataverse_url_builds_an_openable_list_link()
    {
        var launcher = new FakeUrlLauncher();
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader(),
            activeEnv: () => EnvWith("https://contoso.crm.dynamics.com"), launcher: launcher);
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.SelectedTable = vm.Tables.First();

        Assert.True(vm.HasSelectionLink);
        var url = vm.SelectedTableUrl;
        Assert.NotNull(url);
        Assert.Contains("pagetype=entitylist", url!);
        Assert.Contains("etn=" + vm.Tables.First().LogicalName, url);

        await vm.OpenInDataverseCommand.ExecuteAsync(null);
        Assert.Equal(url, launcher.LastUrl);
    }

    [Fact]
    public async Task Without_a_dataverse_url_the_open_link_is_unavailable()
    {
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader(), activeEnv: () => EnvWith(null));
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedTable = vm.Tables.First();

        Assert.False(vm.HasSelectionLink);
        Assert.Null(vm.SelectedTableUrl);
        Assert.False(vm.OpenInDataverseCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_reader_error_surfaces_and_leaves_the_list_empty()
    {
        var vm = new VirtualTablesViewModel(new ErrorReader());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.True(vm.HasLoadError);
        Assert.Equal("boom", vm.LoadError);
        Assert.Empty(vm.Tables);
    }

    [Fact]
    public async Task Search_filters_by_name()
    {
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader());
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.Search = "Vendor";

        Assert.Single(vm.Filtered);
    }
}
