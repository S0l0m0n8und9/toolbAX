using System;
using System.Linq;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

public class QueryBuilderViewModelTests
{
    private static QueryBuilderViewModel MakeVm() =>
        new(new FakeMetadataService(), new FakeODataClient());

    [Fact]
    public void Cached_entity_populates_field_chips_and_query_url()
    {
        var vm = MakeVm();

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.HasFields);
        Assert.NotEmpty(vm.Fields);
        Assert.Contains("/data/CustomersV3", vm.QueryUrl);
        Assert.Contains("$select=", vm.QueryUrl);
        // PK fields are selected by default, so the URL carries a key.
        Assert.Contains("CustomerAccount", vm.QueryUrl);
    }

    [Fact]
    public void Toggling_a_field_updates_the_select_clause_both_ways()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
        var chip = vm.Fields.Single(f => f.Name == "OrganizationName");

        vm.ToggleFieldCommand.Execute(chip);
        Assert.True(chip.IsSelected);
        Assert.Contains("OrganizationName", vm.QueryUrl);

        vm.ToggleFieldCommand.Execute(chip);
        Assert.False(chip.IsSelected);
        Assert.DoesNotContain("OrganizationName", vm.QueryUrl);
    }

    [Fact]
    public void Uncached_entity_hides_fields_and_shows_run_once_hint()
    {
        var vm = MakeVm();

        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2");

        Assert.False(vm.HasFields);
        Assert.Empty(vm.Fields);
        Assert.Contains("run once", vm.NotCachedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_populates_result_rows_columns_and_status()
    {
        var vm = MakeVm();
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.HasRun);
        Assert.NotEmpty(vm.ResultRows);
        Assert.Equal(vm.RowCount, vm.ResultRows.Count);
        Assert.Contains("200", vm.StatusText);
        // Result columns mirror the currently selected fields, in order.
        Assert.Equal(vm.Fields.Where(f => f.IsSelected).Select(f => f.Name), vm.ResultColumns);
        Assert.False(vm.IsBusy);
    }
}
