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
        var uat = shell.Environments.Single(e => e.Id == "uat");

        shell.SetActiveEnvironmentCommand.Execute(uat);

        Assert.Equal("uat", shell.ActiveEnvironment.Id);
    }
}
