using System.Linq;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Pure VM logic for the shell — fast, headless, no view (control-map §0).</summary>
public class ShellViewModelTests
{
    [Fact]
    public void Default_tool_is_the_plugins_home()
    {
        var shell = new ShellViewModel();
        Assert.Equal("home", shell.CurrentTool.Id);
        Assert.Equal(8, shell.Tools.Count);
    }

    [Fact]
    public void Opening_the_palette_clears_the_query_and_shows_all_tools()
    {
        var shell = new ShellViewModel();
        shell.Palette.Query = "ops";

        shell.OpenCommandPaletteCommand.Execute(null);

        Assert.True(shell.IsCommandPaletteOpen);
        Assert.Equal(string.Empty, shell.Palette.Query);
        Assert.Equal(shell.Tools.Count, shell.Palette.FilteredCommands.Count);
    }

    [Fact]
    public void Palette_query_filters_by_title()
    {
        var shell = new ShellViewModel();
        shell.Palette.Query = "compare";

        Assert.Single(shell.Palette.FilteredCommands);
        Assert.Equal("compare", shell.Palette.FilteredCommands[0].Id);
    }

    [Fact]
    public void Invoking_a_palette_command_navigates_and_closes_the_palette()
    {
        var shell = new ShellViewModel();
        shell.OpenCommandPaletteCommand.Execute(null);
        var ops = shell.Tools.Single(t => t.Id == "ops");

        shell.Palette.InvokeCommand.Execute(ops);

        Assert.Equal("ops", shell.CurrentTool.Id);
        Assert.False(shell.IsCommandPaletteOpen);
    }

    [Fact]
    public void SetActiveEnvironment_changes_the_active_environment()
    {
        var shell = new ShellViewModel();
        var uat = shell.Environments.Single(e => e.Id == "uat-eur");

        shell.SetActiveEnvironmentCommand.Execute(uat);

        Assert.Equal("uat-eur", shell.ActiveEnvironment.Id);
    }

    [Fact]
    public void Selecting_profiles_routes_to_the_profiles_screen()
    {
        var shell = new ShellViewModel();
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
    }

    [Fact]
    public void Activating_a_profile_in_profiles_updates_the_shell_switcher()
    {
        // Shell + Profiles share one IProfileStore, and Profiles' SetActive syncs the shell switcher.
        var shell = new ShellViewModel();
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);

        profiles.Selected = profiles.Profiles.Single(p => p.Id == "uat-eur");
        profiles.SetActiveCommand.Execute(null);

        Assert.Equal("uat-eur", shell.ActiveEnvironment.Id);
    }

    [Fact]
    public void Default_content_is_a_placeholder_for_the_home_tool()
    {
        var shell = new ShellViewModel();
        var placeholder = Assert.IsType<PlaceholderScreenViewModel>(shell.CurrentContent);
        Assert.Equal("Plugins", placeholder.Title);
    }

    [Fact]
    public void Selecting_operations_routes_to_the_ops_screen_via_the_factory()
    {
        var built = 0;
        var shell = new ShellViewModel(() => { built++; return new PlaceholderScreenViewModel("ops-stub"); });

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "ops");
        var content = Assert.IsType<PlaceholderScreenViewModel>(shell.CurrentContent);
        Assert.Equal("ops-stub", content.Title);

        // Built once and cached across re-navigation.
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "ops");
        Assert.Equal(1, built);
    }

    [Fact]
    public void Other_tools_route_to_a_titled_placeholder()
    {
        var shell = new ShellViewModel();
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "home");
        var placeholder = Assert.IsType<PlaceholderScreenViewModel>(shell.CurrentContent);
        Assert.Equal("Plugins", placeholder.Title);
    }
}
