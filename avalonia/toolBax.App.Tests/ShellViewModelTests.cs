using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Models;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Pure VM logic for the shell — fast, headless, no view (control-map §0).</summary>
public class ShellViewModelTests
{
    // Records the verb of the last call so a test can prove the shell handed THIS client (not a
    // hidden per-VM FakeODataClient fallback) to the Query / POST builders.
    private sealed class RecordingODataClient : IODataClient
    {
        public string? LastMethod { get; private set; }

        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            LastMethod = method;
            return Task.FromResult(new ODataResponse(200, "OK", "{\"value\":[]}", 1));
        }
    }

    [Fact]
    public async Task Shell_routes_its_odata_client_into_the_post_builder()
    {
        var recorder = new RecordingODataClient();
        // Auto-confirm the send so the routing assertion runs headless (the real DialogService would open
        // a ConfirmWindow); the confirm-gate behaviour itself is covered in PostBuilderViewModelTests.
        var shell = new ShellViewModel(odataClient: recorder, dialogs: new AutoConfirmDialogs());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "post");
        var post = Assert.IsType<PostBuilderViewModel>(shell.CurrentContent);

        await post.SendCommand.ExecuteAsync(null);

        Assert.Equal("POST", recorder.LastMethod);
    }

    [Fact]
    public async Task Shell_routes_its_odata_client_into_the_query_builder()
    {
        var recorder = new RecordingODataClient();
        var shell = new ShellViewModel(odataClient: recorder);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var query = Assert.IsType<QueryBuilderViewModel>(shell.CurrentContent);

        await query.RunCommand.ExecuteAsync(null);

        Assert.Equal("GET", recorder.LastMethod);
    }

    // Returns a single distinctive entity so a test can prove the shell handed THIS metadata service
    // (not a hidden per-VM FakeMetadataService) to the Metadata Browser / Query Builder.
    private sealed class OneEntityMetadata : IMetadataService
    {
        public IReadOnlyList<EntitySet> GetEntities() =>
            new[] { new EntitySet("ZZTopEntity", "M", 1, "k", false, "t") };
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) => Task.FromResult(false);
    }

    [Fact]
    public void Shell_routes_its_metadata_service_into_the_metadata_browser()
    {
        var shell = new ShellViewModel(metadataService: new OneEntityMetadata());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "metadata");
        var metadata = Assert.IsType<MetadataViewModel>(shell.CurrentContent);

        Assert.Contains(metadata.Entities, e => e.Name == "ZZTopEntity");
    }

    [Fact]
    public void Shell_routes_its_metadata_service_into_the_query_builder()
    {
        var shell = new ShellViewModel(metadataService: new OneEntityMetadata());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var query = Assert.IsType<QueryBuilderViewModel>(shell.CurrentContent);

        Assert.Contains(query.Entities, e => e.Name == "ZZTopEntity");
    }

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
        var shell = new ShellViewModel(dialogs: new StubDialogs());
        var uat = shell.Environments.Single(e => e.Id == "uat-eur");

        shell.SetActiveEnvironmentCommand.Execute(uat);

        Assert.Equal("uat-eur", shell.ActiveEnvironment!.Id);
    }

    private sealed class ThrowingDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(ConfirmRequest request) =>
            throw new InvalidOperationException("dialog window closed");
    }

    [Fact]
    public async Task Switching_environment_survives_a_refresh_prompt_failure()
    {
        // The ActiveChanged path is fire-and-forget, so a dialog failure must be handled, not left as an
        // unobserved/faulting task. The environment switch is committed regardless; only the refresh is skipped.
        var shell = new ShellViewModel(dialogs: new ThrowingDialogs());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);
        await shell.SetActiveEnvironmentCommand.ExecutionTask!; // must complete, not fault

        Assert.Equal(other.Id, shell.ActiveEnvironment!.Id); // switch committed
        Assert.Same(before, shell.CurrentContent);           // refresh skipped on failure
    }

    // Records confirm requests so a test can prove the refresh prompt is shown, and returns a fixed answer.
    private sealed class RecordingDialogs : IDialogService
    {
        private readonly bool _answer;
        public int Calls { get; private set; }
        public ConfirmRequest? Last { get; private set; }
        public RecordingDialogs(bool answer) => _answer = answer;
        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Calls++;
            Last = request;
            return Task.FromResult(_answer);
        }
    }

    [Fact]
    public void Confirming_the_refresh_prompt_rebuilds_the_open_data_tool_against_the_new_profile()
    {
        var shell = new ShellViewModel(dialogs: new AutoConfirmDialogs());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);

        // Confirmed → the open tool is rebuilt so its cached metadata/results reflect the new environment.
        Assert.IsType<QueryBuilderViewModel>(shell.CurrentContent);
        Assert.NotSame(before, shell.CurrentContent);
    }

    [Fact]
    public void Declining_the_refresh_prompt_keeps_the_open_tool()
    {
        var shell = new ShellViewModel(dialogs: new StubDialogs()); // declines
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);

        Assert.Same(before, shell.CurrentContent); // declined → unsaved tool state is preserved
        Assert.Equal(other.Id, shell.ActiveEnvironment!.Id); // …but the active environment still switched
    }

    [Fact]
    public void Switching_environment_prompts_before_refreshing_tools()
    {
        var dialogs = new RecordingDialogs(answer: false);
        var shell = new ShellViewModel(dialogs: dialogs);
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);

        Assert.Equal(1, dialogs.Calls);
        Assert.Contains(other.Name, dialogs.Last!.Message);
    }

    [Fact]
    public void Switching_environment_does_not_evict_the_previous_session()
    {
        // Narrowed eviction: a plain environment switch must NOT clear cached sign-ins (that would force a
        // browser re-auth on every switch when one app registration spans many environments).
        var auth = new FakeAuthService();
        var shell = new ShellViewModel(authService: auth, dialogs: new AutoConfirmDialogs());
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);

        Assert.Null(auth.LastSignedOut);
    }

    [Fact]
    public void Reselecting_the_active_environment_does_not_rebuild_or_prompt()
    {
        var dialogs = new RecordingDialogs(answer: true);
        var shell = new ShellViewModel(dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;
        var active = shell.ActiveEnvironment!;

        shell.SetActiveEnvironmentCommand.Execute(active);

        Assert.Same(before, shell.CurrentContent); // not rebuilt — same environment
        Assert.Equal(0, dialogs.Calls);            // no prompt for a no-op reselect
    }

    [Fact]
    public void Switching_environment_preserves_the_profiles_screen_instance()
    {
        // The Profiles screen owns the env switcher + its event subscriptions; rebuilding it would
        // double-subscribe. It must survive a refresh even though data tools are rebuilt.
        var shell = new ShellViewModel(dialogs: new AutoConfirmDialogs());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = shell.CurrentContent;
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);

        Assert.Same(profiles, shell.CurrentContent);
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
        var shell = new ShellViewModel(dialogs: new StubDialogs());
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
        var shell = new ShellViewModel(dialogs: new StubDialogs());
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
