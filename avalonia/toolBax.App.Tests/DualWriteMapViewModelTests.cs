using System.Linq;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

public class DualWriteMapViewModelTests
{
    private static DualWriteMapViewModel MakeVm() => new(new FakeDualWriteMapService());

    [Fact]
    public void Maps_are_listed_with_a_default_selection()
    {
        var vm = MakeVm();

        Assert.NotEmpty(vm.Maps);
        Assert.NotNull(vm.SelectedMap);
    }

    [Fact]
    public void Selecting_a_detailed_map_loads_bindings_value_maps_and_kpis()
    {
        var vm = MakeVm();

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "cust-account");

        Assert.True(vm.HasBindings);
        Assert.NotEmpty(vm.Bindings);
        Assert.Contains(vm.Bindings, b => b.IsKey && b.FoField == "CustomerAccount");
        Assert.NotEmpty(vm.ValueMaps);
        Assert.NotEmpty(vm.Activity);
        Assert.False(string.IsNullOrWhiteSpace(vm.LatencyP95));
    }

    [Fact]
    public void Map_without_cached_bindings_shows_an_empty_state()
    {
        var vm = MakeVm();

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "vend-account");

        Assert.False(vm.HasBindings);
        Assert.Empty(vm.Bindings);
    }

    [Fact]
    public void Search_filters_by_fo_or_dv_entity()
    {
        var vm = MakeVm();

        vm.Search = "Customers";
        Assert.Single(vm.Filtered);

        vm.Search = "account"; // dv "account" on cust-account
        Assert.NotEmpty(vm.Filtered);
    }

    [Fact]
    public void Filtering_out_the_selected_map_keeps_the_detail()
    {
        var vm = MakeVm();
        vm.SelectedMap = vm.Maps.Single(m => m.Id == "cust-account");
        Assert.True(vm.HasBindings);

        // The ListBox nulls SelectedItem when the current row leaves the filtered set; the detail
        // pane must not be wiped.
        vm.SelectedMap = null;

        Assert.Equal("cust-account", vm.DetailMap!.Id);
        Assert.True(vm.HasBindings);
        Assert.NotEmpty(vm.Bindings);
    }

    [Fact]
    public void Errors_indicator_reflects_the_selected_map_error_count()
    {
        var vm = MakeVm();

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "so-salesorder");
        Assert.True(vm.HasErrors);

        vm.SelectedMap = vm.Maps.Single(m => m.Id == "vend-account");
        Assert.False(vm.HasErrors);
    }
}
