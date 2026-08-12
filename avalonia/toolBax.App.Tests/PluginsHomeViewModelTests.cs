using System.Linq;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

public class PluginsHomeViewModelTests
{
    private static PluginsHomeViewModel MakeVm(string? env = "USMF Dev") =>
        new(new BuiltInToolCatalog(), env, openTool: null);

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
        var vm = new PluginsHomeViewModel(new BuiltInToolCatalog(), null, id => opened = id);

        vm.OpenPluginCommand.Execute("query");

        Assert.Equal("query", opened);
    }

    [Fact]
    public void Catalog_lists_the_virtual_tables_tool()
    {
        // #168: the tool shipped in the nav rail and the palette but had no landing-grid card.
        var card = Assert.Single(new BuiltInToolCatalog().Plugins, p => p.Id == "virtualtables");

        Assert.Contains("Virtual Tables", card.Name);
        Assert.Equal("Integration", card.Category);
        Assert.Equal("Alt+V", card.ShortcutLabel);  // same accelerator as the nav-rail item
        Assert.False(card.OperatesLive);            // read-only inspector — no mutations
        Assert.NotEmpty(card.Description);
    }

    [Fact]
    public void Has_env_is_false_when_no_environment_is_supplied()
    {
        var vm = MakeVm(env: null);

        Assert.False(vm.HasEnv);
    }
}
