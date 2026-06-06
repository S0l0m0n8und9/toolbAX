using System.Linq;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

public class PluginsHomeViewModelTests
{
    private static PluginsHomeViewModel MakeVm(string? env = "USMF Dev") =>
        new(new FakePluginCatalog(), env, openTool: null);

    [Fact]
    public void Lists_plugins_with_the_environment_subtitle()
    {
        var vm = MakeVm();

        Assert.NotEmpty(vm.Plugins);
        Assert.Equal("USMF Dev", vm.EnvName);
    }

    [Fact]
    public void Filter_matches_name_or_description()
    {
        var vm = MakeVm();

        vm.Filter = "Compare";
        Assert.Single(vm.FilteredPlugins);

        vm.Filter = "OData"; // appears in several descriptions
        Assert.True(vm.FilteredPlugins.Count() > 1);
    }

    [Fact]
    public void Open_plugin_invokes_the_open_callback_with_the_id()
    {
        string? opened = null;
        var vm = new PluginsHomeViewModel(new FakePluginCatalog(), null, id => opened = id);

        vm.OpenPluginCommand.Execute("query");

        Assert.Equal("query", opened);
    }

    [Fact]
    public void Unsigned_sample_plugin_is_flagged()
    {
        var vm = MakeVm();

        Assert.Contains(vm.Plugins, p => !p.Signed);
    }
}
