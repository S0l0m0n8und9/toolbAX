using System.Linq;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

public class MetadataViewModelTests
{
    private static MetadataViewModel MakeVm() => new(new FakeMetadataService());

    [Fact]
    public void Cached_entity_populates_fields()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.True(vm.IsCached);
        Assert.NotEmpty(vm.Fields);
        Assert.Contains(vm.Fields, f => f.Name == "CustomerAccount" && f.IsKey);
    }

    [Fact]
    public void Uncached_entity_shows_no_fields_and_a_fetch_hint()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "VendorsV2");

        Assert.False(vm.IsCached);
        Assert.Empty(vm.Fields);
        Assert.Contains("Query Builder", vm.NotCachedMessage);
    }

    [Fact]
    public void Filter_matches_name_or_module()
    {
        var vm = MakeVm();

        vm.Search = "CustomersV3";
        Assert.Single(vm.Filtered);

        vm.Search = "GL";
        Assert.NotEmpty(vm.Filtered);
        Assert.All(vm.Filtered, e => Assert.Equal("GL", e.Module));
    }

    [Fact]
    public void Type_display_formats_string_enum_and_decimal()
    {
        var vm = MakeVm();
        vm.Selected = vm.Entities.Single(e => e.Name == "CustomersV3");

        Assert.Equal("String(20)", vm.Fields.Single(f => f.Name == "CustomerAccount").TypeDisplay);
        Assert.Equal("Enum<NoYes>", vm.Fields.Single(f => f.Name == "IsOneTime").TypeDisplay);
        Assert.Equal("Decimal(32)", vm.Fields.Single(f => f.Name == "CreditLimit").TypeDisplay);
    }
}
