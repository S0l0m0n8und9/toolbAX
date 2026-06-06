using System;
using System.Linq;
using ToolBax.App.Models;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
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

        Assert.Equal("uat-eur", shell.ActiveEnvironment!.Id);
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

        Assert.Equal("uat-eur", shell.ActiveEnvironment!.Id);
    }

    [Fact]
    public void Default_content_is_the_plugins_home()
    {
        var shell = new ShellViewModel();
        Assert.IsType<PluginsHomeViewModel>(shell.CurrentContent);
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
    public void Switching_environment_updates_the_cached_home_subtitle()
    {
        var shell = new ShellViewModel();
        var home = Assert.IsType<PluginsHomeViewModel>(shell.CurrentContent);
        var other = shell.Environments.First(e => e.Name != home.EnvName);

        shell.SetActiveEnvironmentCommand.Execute(other);

        Assert.Equal(other.Name, home.EnvName);
    }

    [Fact]
    public void Shell_wires_its_secret_store_into_profiles()
    {
        var secrets = new FakeSecretStore();
        var shell = new ShellViewModel(secretStore: secrets);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);

        profiles.Selected = profiles.Profiles.First();
        profiles.SecretInput = "spn-secret";
        profiles.SaveSecretCommand.Execute(null);

        // The secret reached the shell's store, not a hidden per-VM fallback.
        Assert.True(secrets.HasSecret(profiles.Selected!.Id));
    }

    [Fact]
    public void Renaming_the_active_profile_refreshes_the_shell_environment()
    {
        var shell = new ShellViewModel();
        var activeId = shell.ActiveEnvironment!.Id;
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);

        profiles.Selected = profiles.Profiles.Single(p => p.Id == activeId);
        profiles.DraftName = "Renamed Env";
        profiles.SaveCommand.Execute(null);

        Assert.Equal("Renamed Env", shell.ActiveEnvironment!.Name);
        Assert.Contains(shell.Environments, e => e.Name == "Renamed Env");
    }

    [Fact]
    public void Adding_then_deleting_a_profile_syncs_the_shell_switcher()
    {
        var shell = new ShellViewModel();
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        var before = shell.Environments.Count;

        profiles.AddProfileCommand.Execute(null);
        var addedId = profiles.Selected!.Id;
        Assert.Equal(before + 1, shell.Environments.Count);
        Assert.Contains(shell.Environments, e => e.Id == addedId);

        profiles.DeleteProfileCommand.Execute(null);
        Assert.Equal(before, shell.Environments.Count);
        Assert.DoesNotContain(shell.Environments, e => e.Id == addedId);
    }

    [Fact]
    public void Deleting_the_active_profile_picks_another_active_environment()
    {
        var shell = new ShellViewModel();
        var activeId = shell.ActiveEnvironment!.Id;
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == activeId);

        profiles.DeleteProfileCommand.Execute(null);

        Assert.NotEqual(activeId, shell.ActiveEnvironment?.Id);
        Assert.DoesNotContain(shell.Environments, e => e.Id == activeId);
    }

    [Fact]
    public void Empty_profile_store_does_not_crash_the_shell()
    {
        var shell = new ShellViewModel(profileStore: new FakeProfileStore(Array.Empty<EnvProfile>()));

        Assert.Empty(shell.Environments);
        Assert.Null(shell.ActiveEnvironment);
        Assert.IsType<PluginsHomeViewModel>(shell.CurrentContent); // still routes the default tool
    }

    [Fact]
    public void Unknown_tools_route_to_a_titled_placeholder()
    {
        var shell = new ShellViewModel();
        shell.CurrentTool = new NavTool("unknown-x", "Unknown Tool", '\0');
        var placeholder = Assert.IsType<PlaceholderScreenViewModel>(shell.CurrentContent);
        Assert.Equal("Unknown Tool", placeholder.Title);
    }
}
