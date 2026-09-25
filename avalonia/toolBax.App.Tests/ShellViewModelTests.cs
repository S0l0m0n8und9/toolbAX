using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
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
        public void Invalidate() { }

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
    public void Shell_binds_Query_and_Metadata_commands_to_the_active_environment_accessor()
    {
        var shell = new ShellViewModel(
            profileStore: new FakeProfileStore(Array.Empty<EnvProfile>()),
            metadataService: new OneEntityMetadata());

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var query = Assert.IsType<QueryBuilderViewModel>(shell.CurrentContent);
        Assert.False(query.RunCommand.CanExecute(null));

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "metadata");
        var metadata = Assert.IsType<MetadataViewModel>(shell.CurrentContent);
        Assert.False(metadata.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void Default_tool_is_the_plugins_home()
    {
        var shell = new ShellViewModel();
        Assert.Equal("home", shell.CurrentTool.Id);
        Assert.Equal(9, shell.Tools.Count); // home, query, ops, mapbrowser, compare, metadata, virtualtables, post, profiles
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
    public async Task A_switch_confirmation_failure_keeps_the_previous_environment_and_tool()
    {
        // Confirmation happens before persistence/header state. A dialog failure is handled and leaves the
        // previous environment and tool intact.
        var shell = new ShellViewModel(dialogs: new ThrowingDialogs());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;
        var previous = shell.ActiveEnvironment!;
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);
        await shell.SetActiveEnvironmentCommand.ExecutionTask!; // must complete, not fault

        Assert.Equal(previous.Id, shell.ActiveEnvironment!.Id);
        Assert.Same(before, shell.CurrentContent);
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

    private sealed class GatedDialogs : IDialogService
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release(bool answer) => _answer.TrySetResult(answer);
        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            _entered.TrySetResult();
            return _answer.Task;
        }
    }

    private sealed class GatedActionConnector : IDualWriteConnector
    {
        public GatedActionGateway Gateway { get; } = new();
        public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult(new DualWriteSession(Gateway, "cid", "Connection", env, "https://gateway.example"));
    }

    private sealed class GatedActionGateway : IDualWriteGateway
    {
        private readonly FakeCoreDualWriteGateway _inner = new(FakeDualWriteConnector.SeedMaps());
        private readonly TaskCompletionSource _startEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartEntered => _startEntered.Task;
        public void ReleaseStart() => _releaseStart.TrySetResult();
        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default) =>
            _inner.GetEnvironmentAsync(foIdentifier, cancellationToken);
        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default) =>
            _inner.GetMapsAsync(cid, cancellationToken);
        public async Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action,
            IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default)
        {
            _startEntered.TrySetResult();
            await _releaseStart.Task.WaitAsync(cancellationToken);
            return await _inner.StartActionAsync(action, maps, cid, cancellationToken);
        }
        public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default) =>
            _inner.GetStatusAsync(requestId, cancellationToken);
        public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId, string templateId, CancellationToken cancellationToken = default) =>
            _inner.SwitchActiveTemplateAsync(cid, projectId, templateId, cancellationToken);
        public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken cancellationToken = default) =>
            _inner.GetFieldMappingsAsync(projectId, cancellationToken);
        public Task RefreshTablesAsync(string fieldMappingName, CancellationToken cancellationToken = default) =>
            _inner.RefreshTablesAsync(fieldMappingName, cancellationToken);
        public Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname, CancellationToken cancellationToken = default) =>
            _inner.GetConnectionSetAsync(cname, cancellationToken);
        public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities,
            bool forceReset, CancellationToken cancellationToken = default) =>
            _inner.ResetLinksAsync(cid, connectionSet, legalEntities, forceReset, cancellationToken);
        public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName,
            IReadOnlyList<string> keyFields, CancellationToken cancellationToken = default) =>
            _inner.ApplyIntegrationKeysAsync(datasetName, ceEntityName, keyFields, cancellationToken);
    }

    private sealed class StartPostDuringSwitchDialogs : IDialogService
    {
        private readonly TaskCompletionSource _postConfirmationEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _postAnswer =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public PostBuilderViewModel? Post { get; set; }
        public Task? PostTask { get; private set; }
        public void ReleasePost(bool answer) => _postAnswer.TrySetResult(answer);

        public async Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                PostTask = Post!.SendCommand.ExecuteAsync(null);
                await _postConfirmationEntered.Task;
                return true;
            }

            _postConfirmationEntered.TrySetResult();
            return await _postAnswer.Task;
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
    public async Task Declining_the_switch_keeps_the_environment_store_and_open_tool()
    {
        var store = new FakeProfileStore();
        var shell = new ShellViewModel(profileStore: store, dialogs: new StubDialogs()); // declines
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;
        var previous = shell.ActiveEnvironment!;
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);
        await shell.SetActiveEnvironmentCommand.ExecutionTask!;

        Assert.Same(before, shell.CurrentContent);
        Assert.Same(previous, shell.ActiveEnvironment);
        Assert.Equal(previous.Id, store.ActiveId);
    }

    [Fact]
    public void Switching_environment_prompts_before_changing_environment_or_tools()
    {
        var dialogs = new RecordingDialogs(answer: false);
        var shell = new ShellViewModel(dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var other = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);

        Assert.Equal(1, dialogs.Calls);
        Assert.Contains(other.Name, dialogs.Last!.Message);
    }

    [Fact]
    public async Task Edited_target_during_confirmation_is_not_activated_or_persisted()
    {
        var store = new FakeProfileStore();
        var dialogs = new GatedDialogs();
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var beforeTool = shell.CurrentContent;
        var previous = shell.ActiveEnvironment!;
        var target = shell.Environments.First(e => e.Id != previous.Id);

        var switching = shell.SetActiveEnvironmentCommand.ExecuteAsync(target);
        await dialogs.Entered;
        store.Save(target with { Url = "https://edited.operations.dynamics.com" });
        dialogs.Release(true);
        await switching;

        Assert.Same(previous, shell.ActiveEnvironment);
        Assert.Equal(previous.Id, store.ActiveId);
        Assert.Same(beforeTool, shell.CurrentContent);
        Assert.Contains("changed while confirmation", shell.BackgroundError);
    }

    [Fact]
    public async Task Deleted_target_during_confirmation_is_not_activated_or_persisted()
    {
        var store = new FakeProfileStore();
        var dialogs = new GatedDialogs();
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var beforeTool = shell.CurrentContent;
        var previous = shell.ActiveEnvironment!;
        var target = shell.Environments.First(e => e.Id != previous.Id);

        var switching = shell.SetActiveEnvironmentCommand.ExecuteAsync(target);
        await dialogs.Entered;
        store.Delete(target.Id);
        dialogs.Release(true);
        await switching;

        Assert.Same(previous, shell.ActiveEnvironment);
        Assert.Equal(previous.Id, store.ActiveId);
        Assert.Same(beforeTool, shell.CurrentContent);
        Assert.Contains("deleted while confirmation", shell.BackgroundError);
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
    public async Task Header_switch_updates_an_already_open_profiles_active_id()
    {
        var shell = new ShellViewModel(dialogs: new AutoConfirmDialogs());
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        var target = shell.Environments.First(e => e.Id != shell.ActiveEnvironment!.Id);

        await shell.SetActiveEnvironmentCommand.ExecuteAsync(target);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == target.Id);

        Assert.Equal(target.Id, profiles.ActiveId);
        Assert.True(profiles.IsSelectedActive);
    }

    [Fact]
    public async Task Declining_active_identity_save_preserves_profile_and_drafts()
    {
        var store = new FakeProfileStore();
        var shell = new ShellViewModel(profileStore: store, dialogs: new RecordingDialogs(answer: false));
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        var active = shell.ActiveEnvironment!;
        profiles.Selected = profiles.Profiles.Single(p => p.Id == active.Id);
        profiles.DraftUrl = "https://edited.operations.dynamics.com";

        await profiles.SaveCommand.ExecuteAsync(null);

        Assert.Equal(active.Url, store.GetAll().Single(p => p.Id == active.Id).Url);
        Assert.Equal(active.Url, shell.ActiveEnvironment!.Url);
        Assert.Equal("https://edited.operations.dynamics.com", profiles.DraftUrl);
        Assert.Equal(active.Url, profiles.Profiles.Single(p => p.Id == active.Id).Url);
        Assert.Contains("cancelled", profiles.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirmed_active_company_save_invalidates_open_tools()
    {
        var dialogs = new RecordingDialogs(answer: true);
        var shell = new ShellViewModel(dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var queryBefore = shell.CurrentContent;
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == shell.ActiveEnvironment!.Id);
        profiles.DraftLegal = "DEMF";

        await profiles.SaveCommand.ExecuteAsync(null);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");

        Assert.Equal("DEMF", shell.ActiveEnvironment!.Legal);
        Assert.NotSame(queryBefore, shell.CurrentContent);
        Assert.Equal(1, dialogs.Calls);
    }

    [Fact]
    public async Task Declined_active_legacy_auth_replacement_preserves_profile_shell_and_supported_draft()
    {
        var legacy = new EnvProfile("legacy", "Legacy", "https://legacy.operations.dynamics.com", "tenant",
            "USMF", "Tier 1", EnvStatus.Disconnected, ClientId: "same-client", AuthMode: FoAuthMode.Certificate);
        var other = new EnvProfile("other", "Other", "https://other.operations.dynamics.com", "tenant",
            "DEMF", "Tier 1", EnvStatus.Disconnected);
        var store = new FakeProfileStore(new[] { legacy, other }) { ActiveId = legacy.Id };
        var dialogs = new RecordingDialogs(answer: false);
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var queryBefore = shell.CurrentContent;
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == legacy.Id);
        profiles.SelectedFoAuthMode = FoAuthMode.ClientSecret;

        await profiles.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Calls);
        Assert.Equal(FoAuthMode.Certificate, Assert.Single(store.GetAll(), p => p.Id == legacy.Id).AuthMode);
        Assert.Equal(FoAuthMode.Certificate, shell.ActiveEnvironment!.AuthMode);
        Assert.Equal(FoAuthMode.ClientSecret, profiles.DraftAuthMode);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        Assert.Same(queryBefore, shell.CurrentContent);
    }

    [Fact]
    public async Task Pending_POST_confirmation_blocks_switch_active_save_and_active_delete()
    {
        var store = new FakeProfileStore();
        var dialogs = new GatedDialogs();
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "post");
        var post = Assert.IsType<PostBuilderViewModel>(shell.CurrentContent);
        post.Method = "DELETE";
        post.Path = "/data/CustomersV3(dataAreaId='USMF',CustomerAccount='US-1')";

        var send = post.SendCommand.ExecuteAsync(null);
        await dialogs.Entered;
        Assert.True(post.MutationInProgress);
        var active = shell.ActiveEnvironment!;
        var other = shell.Environments.First(e => e.Id != active.Id);

        await shell.SetActiveEnvironmentCommand.ExecuteAsync(other);
        Assert.Same(active, shell.ActiveEnvironment);
        Assert.Equal(active.Id, store.ActiveId);

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == active.Id);
        profiles.DraftUrl = "https://edited.operations.dynamics.com";
        await profiles.SaveCommand.ExecuteAsync(null);
        Assert.Equal(active.Url, store.GetAll().Single(p => p.Id == active.Id).Url);

        profiles.DeleteProfileCommand.Execute(null);
        Assert.Contains(store.GetAll(), p => p.Id == active.Id);
        Assert.Contains("live write", profiles.Status, StringComparison.OrdinalIgnoreCase);

        dialogs.Release(false);
        await send;
        Assert.False(post.MutationInProgress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing-profile")]
    public async Task Effective_startup_profile_is_active_in_Profiles_mutation_guards(string? persistedActiveId)
    {
        var store = new FakeProfileStore { ActiveId = persistedActiveId };
        var dialogs = new GatedDialogs();
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        var effective = shell.ActiveEnvironment!;
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "post");
        var post = Assert.IsType<PostBuilderViewModel>(shell.CurrentContent);
        post.Method = "DELETE";
        post.Path = "/data/CustomersV3(dataAreaId='USMF',CustomerAccount='US-1')";
        var send = post.SendCommand.ExecuteAsync(null);
        await dialogs.Entered;

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == effective.Id);
        Assert.Equal(effective.Id, profiles.ActiveId);
        Assert.True(profiles.IsSelectedActive);

        profiles.DraftUrl = "https://edited.operations.dynamics.com";
        await profiles.SaveCommand.ExecuteAsync(null);
        Assert.Equal(effective.Url, store.GetAll().Single(p => p.Id == effective.Id).Url);

        profiles.DeleteProfileCommand.Execute(null);
        Assert.Contains(store.GetAll(), p => p.Id == effective.Id);
        Assert.Contains("live write", profiles.Status, StringComparison.OrdinalIgnoreCase);

        dialogs.Release(false);
        await send;
    }

    [Fact]
    public async Task Accepted_active_save_updates_the_captured_profile_without_relabelling_a_new_selection()
    {
        var store = new FakeProfileStore();
        var dialogs = new GatedDialogs();
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        var active = shell.ActiveEnvironment!;
        var other = profiles.Profiles.First(p => p.Id != active.Id);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == active.Id);
        profiles.DraftUrl = "https://edited.operations.dynamics.com";

        var saving = profiles.SaveCommand.ExecuteAsync(null);
        await dialogs.Entered;
        profiles.Selected = other;
        dialogs.Release(true);
        await saving;

        Assert.Equal("https://edited.operations.dynamics.com", store.GetAll().Single(p => p.Id == active.Id).Url);
        Assert.Equal(other.Url, store.GetAll().Single(p => p.Id == other.Id).Url);
        Assert.Equal(other.Id, profiles.Selected!.Id);
        Assert.Equal(other.Url, profiles.DraftUrl);
        Assert.Equal("https://edited.operations.dynamics.com", shell.ActiveEnvironment!.Url);
    }

    [Fact]
    public async Task Mutation_starting_while_switch_confirmation_is_open_blocks_the_commit()
    {
        var store = new FakeProfileStore();
        var dialogs = new StartPostDuringSwitchDialogs();
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "post");
        var post = Assert.IsType<PostBuilderViewModel>(shell.CurrentContent);
        post.Method = "DELETE";
        post.Path = "/data/CustomersV3(dataAreaId='USMF',CustomerAccount='US-1')";
        dialogs.Post = post;
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var previous = shell.ActiveEnvironment!;
        var target = shell.Environments.First(e => e.Id != previous.Id);

        await shell.SetActiveEnvironmentCommand.ExecuteAsync(target);

        Assert.True(post.MutationInProgress);
        Assert.Same(previous, shell.ActiveEnvironment);
        Assert.Equal(previous.Id, store.ActiveId);
        Assert.Contains("live write", shell.BackgroundError, StringComparison.OrdinalIgnoreCase);

        dialogs.ReleasePost(false);
        await dialogs.PostTask!;
        Assert.False(post.MutationInProgress);
    }

    [Fact]
    public async Task Submitted_operations_action_keeps_shell_switch_blocked_after_competing_debug_is_refused()
    {
        var dialogs = new RecordingDialogs(answer: true);
        var connector = new GatedActionConnector();
        var odata = new RecordingODataClient();
        ShellViewModel? shell = null;
        var operations = new DualWriteOpsViewModel(connector, () => shell?.ActiveEnvironment, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: new OneEntityMetadata());
        shell = new ShellViewModel(operationsContentFactory: () => operations, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "ops");
        await operations.LoadCommand.ExecuteAsync(null);
        operations.Maps.First(m => m.State == "Running").IsSelected = true;
        var previous = shell.ActiveEnvironment!;
        var target = shell.Environments.First(e => e.Id != previous.Id);

        var action = operations.RunActionCommand.ExecuteAsync(operations.StopAction);
        await connector.Gateway.StartEntered;

        await operations.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await shell.SetActiveEnvironmentCommand.ExecuteAsync(target);
        var stillOwned = operations.MutationInProgress;
        var stillBusy = operations.IsBusy;
        var activeAfterAttempt = shell.ActiveEnvironment;
        var dialogCalls = dialogs.Calls;
        var odataMethod = odata.LastMethod;

        connector.Gateway.ReleaseStart();
        await action;

        Assert.True(stillOwned);
        Assert.True(stillBusy);
        Assert.Same(previous, activeAfterAttempt);
        Assert.Equal(1, dialogCalls);
        Assert.Null(odataMethod);
        Assert.Contains("live write", shell.BackgroundError, StringComparison.OrdinalIgnoreCase);
        Assert.False(operations.MutationInProgress);
        Assert.False(operations.IsBusy);
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
    public void Deleting_the_active_profile_persists_the_replacement_and_refreshes_the_open_tools()
    {
        // #154: the store clears the persisted default when the active profile is deleted, so the shell
        // must write the replacement back — otherwise the next launch starts with no active environment
        // — and the open tools must drop the deleted environment's cached data.
        var store = new FakeProfileStore();
        var dialogs = new RecordingDialogs(answer: false);
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        var activeId = shell.ActiveEnvironment!.Id;

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.Single(p => p.Id == activeId);

        // Park on a data tool so its cached VM identity shows whether the tools were rebuilt.
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;

        profiles.DeleteProfileCommand.Execute(null);

        var replacement = shell.ActiveEnvironment;
        Assert.NotNull(replacement);
        Assert.NotEqual(activeId, replacement!.Id);
        Assert.Equal(replacement.Id, store.ActiveId);  // persisted → the next launch is coherent
        Assert.NotSame(before, shell.CurrentContent);  // deleted env's data is gone from the open tool
        Assert.IsType<QueryBuilderViewModel>(shell.CurrentContent);
        Assert.Equal(0, dialogs.Calls);                // no "refresh open tools?" prompt for a deletion
    }

    [Fact]
    public void Deleting_a_non_active_profile_leaves_the_active_id_and_open_tools_alone()
    {
        var store = new FakeProfileStore();
        var dialogs = new RecordingDialogs(answer: false);
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        var activeId = shell.ActiveEnvironment!.Id;

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.First(p => p.Id != activeId);
        var deletedId = profiles.Selected!.Id;

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;

        profiles.DeleteProfileCommand.Execute(null);

        Assert.DoesNotContain(shell.Environments, e => e.Id == deletedId);
        Assert.Equal(activeId, shell.ActiveEnvironment!.Id);
        Assert.Equal(activeId, store.ActiveId);       // untouched — the active profile didn't change
        Assert.Same(before, shell.CurrentContent);    // …so the open tool's state is preserved
        Assert.Equal(0, dialogs.Calls);
    }

    [Fact]
    public void Deleting_the_last_remaining_profile_leaves_no_active_environment()
    {
        // The Profiles screen keeps at least one profile (CanDeleteProfile), so the shell's
        // "no replacement" branch is defensive. Park a decoy in the screen's own list to get past that
        // invariant; the store and the shell still hold exactly one real profile, so the end state is
        // the genuine one — empty store, no persisted default, nothing for the tools to show.
        var only = new EnvProfile("solo", "Solo Env", "solo.operations.dynamics.com", "t", "USMF", "Tier 1", EnvStatus.Disconnected);
        var store = new FakeProfileStore(new[] { only }) { ActiveId = only.Id };
        var shell = new ShellViewModel(profileStore: store, dialogs: new RecordingDialogs(answer: false));

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Profiles.Add(new EnvProfile("decoy", "Decoy", "d", "t", "L", "Tier 1", EnvStatus.Disconnected));
        profiles.Selected = profiles.Profiles.Single(p => p.Id == only.Id);

        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;

        profiles.DeleteProfileCommand.Execute(null);

        Assert.Empty(shell.Environments);
        Assert.Null(shell.ActiveEnvironment);
        Assert.Null(store.ActiveId);                   // correctly stays cleared — nothing to point at
        Assert.NotSame(before, shell.CurrentContent);  // tools rebuilt: the deleted env's data is gone
    }

    [Fact]
    public void Empty_profile_store_does_not_crash_the_shell()
    {
        var shell = new ShellViewModel(profileStore: new FakeProfileStore(Array.Empty<EnvProfile>()));

        Assert.Empty(shell.Environments);
        Assert.Null(shell.ActiveEnvironment);
        Assert.IsType<PluginsHomeViewModel>(shell.CurrentContent); // still routes the default tool
    }

    // A store that can read but not persist the active id (e.g. a locked profile.db).
    private sealed class UnwritableActiveIdStore : IProfileStore
    {
        private readonly IReadOnlyList<EnvProfile> _profiles = FakeProfileStore.Seed();
        public IReadOnlyList<EnvProfile> GetAll() => _profiles;
        public void Save(EnvProfile profile) { }
        public void Delete(string id) { }
        public string? ActiveId
        {
            get => _profiles[0].Id;
            set => throw new IOException("profile.db is locked");
        }
    }

    [Fact]
    public async Task A_profile_store_that_cannot_persist_the_active_id_rolls_the_switch_back()
    {
        // The switch is all-or-nothing. A store that rejects the write used to leave the header on the new
        // environment with the tools and the persisted default still on the old one: the shell then lied
        // about where it was pointing, and the only evidence was a trace line. It must roll back and say so.
        // This is also the method behind the fire-and-forget ActiveChanged handler, where a fault becomes an
        // unobserved exception the dispatcher later rethrows — i.e. a dead app (#163).
        var dialogs = new RecordingDialogs(answer: true); // would rebuild the tools if it were ever asked
        var shell = new ShellViewModel(profileStore: new UnwritableActiveIdStore(), dialogs: dialogs);
        var previous = shell.ActiveEnvironment!;
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "query");
        var before = shell.CurrentContent;
        var other = shell.Environments.First(e => e.Id != previous.Id);

        shell.SetActiveEnvironmentCommand.Execute(other);
        await shell.SetActiveEnvironmentCommand.ExecutionTask!; // must complete, not fault

        Assert.Equal(previous.Id, shell.ActiveEnvironment!.Id);      // rolled back: the switch didn't happen
        Assert.Contains("Couldn't switch environment", shell.BackgroundError);
        Assert.Contains("profile.db is locked", shell.BackgroundError); // …with the store's own reason
        Assert.Equal(1, dialogs.Calls);             // discard approved before persistence was attempted
        Assert.Same(before, shell.CurrentContent);  // …and the open tool keeps the environment it has
    }

    // --- Degraded mode + background-failure surface (#163/#164) ---

    [Fact]
    public void Degraded_mode_surfaces_the_reason_in_the_banner_text()
    {
        const string reason = "profile store unavailable: database is locked";
        var shell = new ShellViewModel(degraded: new DegradedMode(reason));

        Assert.True(shell.IsDegraded);
        Assert.Contains("Offline sample data", shell.DegradedBannerText);
        Assert.Contains(reason, shell.DegradedBannerText);
        Assert.Contains("Nothing on screen is live", shell.DegradedBannerText);
    }

    [Fact]
    public void A_healthy_shell_is_not_degraded()
    {
        var shell = new ShellViewModel();

        Assert.False(shell.IsDegraded);
        Assert.Equal(string.Empty, shell.DegradedBannerText);
    }

    [Fact]
    public void Reporting_a_background_failure_surfaces_it_in_the_status_strip()
    {
        var shell = new ShellViewModel();
        Assert.Equal(string.Empty, shell.BackgroundError);

        shell.ReportBackgroundFailure("A background action failed: clipboard is busy");

        Assert.Equal("A background action failed: clipboard is busy", shell.BackgroundError);
    }

    // --- Nav rail / Plugins-home coherence (#168) ---

    [Fact]
    public void Every_shipped_tool_has_a_plugins_home_card()
    {
        // Virtual Tables shipped into the nav rail and the command palette but never got a landing-grid
        // card, so the home screen quietly under-reported what the app can do. Home IS the grid, so it is
        // the one tool with no card of its own.
        var cardIds = new BuiltInToolCatalog().Plugins.Select(p => p.Id).ToHashSet();
        var missing = new ShellViewModel().Tools
            .Where(t => t.Id != "home" && !cardIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToList();

        Assert.True(missing.Count == 0,
            "Shipped tools with no Plugins-home card: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_plugins_home_card_opens_a_real_tool()
    {
        // The other direction: a card whose id matches no tool is a dead click (OpenToolById ignores it).
        var toolIds = new ShellViewModel().Tools.Select(t => t.Id).ToHashSet();
        var orphans = new BuiltInToolCatalog().Plugins
            .Where(card => !toolIds.Contains(card.Id))
            .Select(card => card.Id)
            .ToList();

        Assert.True(orphans.Count == 0,
            "Plugins-home cards with no matching tool: " + string.Join(", ", orphans));
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
