using System;
using System.Collections.Generic;
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

public sealed class AsyncProfileUiTests
{
    private static EnvProfile Profile(string id, string name) => new(
        id, name, $"https://{id}.operations.dynamics.com", "tenant", "USMF", "Tier 1",
        EnvStatus.Disconnected, DataverseUrl: $"https://{id}.crm.dynamics.com",
        ClientId: "fo-client", AuthMode: FoAuthMode.ClientSecret,
        DataverseClientId: "dv-client", DataverseAuthMode: FoAuthMode.ClientSecret);

    [Fact]
    public async Task Pending_save_returns_control_and_preserves_newer_same_id_draft()
    {
        var store = new ControlledProfileStore(Profile("a", "Saved"), Profile("b", "Other"))
        {
            SaveRelease = NewGate()
        };
        using var vm = new ProfilesViewModel(store);
        vm.DraftName = "Submitted";

        var saving = vm.SaveCommand.ExecuteAsync(null);
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(vm.IsPersisting);
        Assert.False(saving.IsCompleted);
        vm.DraftName = "Newer draft";
        store.SaveRelease.SetResult(true);
        await saving;

        Assert.Equal("Submitted", store.GetAll().Single(profile => profile.Id == "a").Name);
        Assert.Equal("Submitted", vm.Profiles.Single(profile => profile.Id == "a").Name);
        Assert.Equal("Newer draft", vm.DraftName);
        Assert.False(vm.IsPersisting);
    }

    [Fact]
    public async Task Failed_save_leaves_prior_list_and_selection_and_reports_captured_profile()
    {
        var original = Profile("a", "Captured");
        var store = new ControlledProfileStore(original, Profile("b", "Other"))
        {
            SaveError = new InvalidOperationException("late write failure")
        };
        using var vm = new ProfilesViewModel(store);
        vm.DraftName = "Attempted";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Same(original, vm.Selected);
        Assert.Same(original, vm.Profiles.Single(profile => profile.Id == "a"));
        Assert.Contains("Attempted", vm.Status);
        Assert.Contains("late write failure", vm.Status);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("activate")]
    [InlineData("clear")]
    public async Task Remaining_mutation_commands_are_nonblocking_and_nonqueue_while_pending(string command)
    {
        var store = new ControlledProfileStore(Profile("a", "A"), Profile("b", "B"));
        var secrets = new ControlledSecretStore();
        using var vm = new ProfilesViewModel(store, secrets);
        Task pending;
        Task started;
        TaskCompletionSource<bool> release;
        switch (command)
        {
            case "add":
                store.SaveRelease = release = NewGate();
                pending = vm.AddProfileCommand.ExecuteAsync(null);
                started = store.SaveStarted.Task;
                break;
            case "delete":
                vm.Selected = vm.Profiles.Single(profile => profile.Id == "b");
                store.DeleteRelease = release = NewGate();
                pending = vm.DeleteProfileCommand.ExecuteAsync(null);
                started = store.DeleteStarted.Task;
                break;
            case "activate":
                vm.Selected = vm.Profiles.Single(profile => profile.Id == "b");
                store.ActiveRelease = release = NewGate();
                pending = vm.SetActiveCommand.ExecuteAsync(null);
                started = store.ActiveStarted.Task;
                break;
            default:
                secrets.ClearRelease = release = NewGate();
                pending = vm.ClearSecretCommand.ExecuteAsync(null);
                started = secrets.ClearStarted.Task;
                break;
        }

        await started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(vm.IsPersisting);
        Assert.False(pending.IsCompleted);
        if (command == "add") vm.AddProfileCommand.Execute(null);
        else if (command == "delete") vm.DeleteProfileCommand.Execute(null);
        else if (command == "activate") vm.SetActiveCommand.Execute(null);
        else vm.ClearSecretCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(1, command switch
        {
            "add" => store.SaveCalls,
            "delete" => store.DeleteCalls,
            "activate" => store.ActiveCalls,
            _ => secrets.ClearCalls,
        });
        release.SetResult(true);
        await pending;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.IsPersisting && DateTime.UtcNow < deadline)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        Assert.False(vm.IsPersisting);
        Assert.Equal(1, command switch
        {
            "add" => store.SaveCalls,
            "delete" => store.DeleteCalls,
            "activate" => store.ActiveCalls,
            _ => secrets.ClearCalls,
        });
    }

    [Fact]
    public async Task Dispose_cancels_pending_save_without_late_publication()
    {
        var store = new ControlledProfileStore(Profile("a", "Saved"), Profile("b", "Other"))
        {
            SaveRelease = NewGate()
        };
        var vm = new ProfilesViewModel(store);
        var published = false;
        vm.ProfileSaved += _ => published = true;
        vm.DraftName = "Attempted";
        var saving = vm.SaveCommand.ExecuteAsync(null);
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        vm.Dispose();
        await saving.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(published);
        Assert.Equal("Saved", store.GetAll().Single(profile => profile.Id == "a").Name);
    }

    [Fact]
    public async Task Secret_noop_preserves_input_and_committed_true_clears_only_owned_input()
    {
        var secrets = new ControlledSecretStore { SetResult = false };
        using var vm = new ProfilesViewModel(new ControlledProfileStore(Profile("a", "Saved")), secrets);
        vm.SecretInput = "first";

        await vm.SaveSecretCommand.ExecuteAsync(null);

        Assert.Equal("first", vm.SecretInput);
        Assert.Contains("client ID", vm.Status);

        secrets.SetResult = true;
        secrets.SetRelease = NewGate();
        vm.SecretInput = "submitted";
        var storing = vm.SaveSecretCommand.ExecuteAsync(null);
        await secrets.SetStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        vm.SecretInput = "newer";
        secrets.SetRelease.SetResult(true);
        await storing;
        Assert.Equal("newer", vm.SecretInput);
        Assert.True(vm.HasSecret);

        secrets.SetRelease = null;
        secrets.ResetSetStarted();
        vm.SecretInput = "owned";
        await vm.SaveSecretCommand.ExecuteAsync(null);
        Assert.Empty(vm.SecretInput);
    }

    [Fact]
    public async Task Presence_refresh_discards_old_selection_and_models_failure_as_unknown()
    {
        var secrets = new ControlledSecretStore { GatePresence = true };
        using var vm = new ProfilesViewModel(
            new ControlledProfileStore(Profile("a", "A"), Profile("b", "B")), secrets);
        await secrets.WaitForPresenceCallsAsync("a", 3);

        vm.Selected = vm.Profiles.Single(profile => profile.Id == "b");
        await secrets.WaitForPresenceCallsAsync("b", 3);
        secrets.CompletePresence("a", true);
        await Task.Yield();
        Assert.Equal(SecretPresenceState.Loading, vm.FoSecretPresence);
        secrets.CompletePresence("b", false);
        await vm.RefreshSecretPresenceCommand.ExecutionTask!.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(SecretPresenceState.Absent, vm.FoSecretPresence);
        Assert.Equal(SecretPresenceState.Absent, vm.DataverseSecretPresence);
        Assert.Equal(SecretPresenceState.Absent, vm.DiSecretPresence);

        secrets.GatePresence = false;
        secrets.PresenceError = new InvalidOperationException("presence unavailable");
        vm.Selected = vm.Profiles.Single(profile => profile.Id == "a");
        await vm.RefreshSecretPresenceCommand.ExecutionTask!.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(SecretPresenceState.Failed, vm.FoSecretPresence);
        Assert.Contains("unavailable", vm.SecretPresenceMessage);
    }

    [Fact]
    public async Task Probe_preflight_uses_fresh_async_presence_instead_of_display_cache()
    {
        var secrets = new FreshProbeSecretStore();
        var tester = new CountingConnectionTester();
        using var vm = new ProfilesViewModel(
            new ControlledProfileStore(Profile("a", "A")), secrets, connectionTester: tester);
        await vm.RefreshSecretPresenceCommand.ExecutionTask!;
        Assert.False(vm.HasSecret);

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(1, tester.FoCalls);
        Assert.Contains("Connected", vm.FoTestStatus);
    }

    [Fact]
    public async Task Committed_secret_presence_wins_over_late_target_read_without_overwriting_other_targets()
    {
        var secrets = new ControlledSecretStore { GatePresence = true };
        using var vm = new ProfilesViewModel(new ControlledProfileStore(Profile("a", "A")), secrets);
        await secrets.WaitForPresenceCallsAsync("a", 3);
        secrets.CompletePresence("a", false, SecretTarget.Dataverse, SecretTarget.DataIntegrator);

        vm.SecretInput = "stored";
        await vm.SaveSecretCommand.ExecuteAsync(null);
        Assert.True(vm.HasSecret);
        secrets.CompletePresence("a", false, SecretTarget.Fo);
        await Task.Yield();
        Assert.True(vm.HasSecret);
        Assert.Equal(SecretPresenceState.Absent, vm.DataverseSecretPresence);
        Assert.Equal(SecretPresenceState.Absent, vm.DiSecretPresence);

        vm.RefreshSecretPresenceCommand.Execute(null);
        await secrets.WaitForPresenceCallsAsync("a", 6);
        secrets.CompletePresence("a", true, SecretTarget.Dataverse);
        secrets.CompletePresence("a", false, SecretTarget.DataIntegrator);
        await vm.ClearSecretCommand.ExecuteAsync(null);
        Assert.False(vm.HasSecret);
        secrets.CompletePresence("a", true, SecretTarget.Fo);
        await vm.RefreshSecretPresenceCommand.ExecutionTask!;

        Assert.False(vm.HasSecret);
        Assert.True(vm.HasDataverseSecret);
        Assert.False(vm.HasDiSecret);
    }

    [Fact]
    public async Task Newer_manual_presence_refresh_wins_when_older_store_reads_ignore_cancellation()
    {
        var secrets = new ControlledSecretStore { GatePresence = true };
        using var vm = new ProfilesViewModel(new ControlledProfileStore(Profile("a", "A")), secrets);
        await secrets.WaitForPresenceCallsAsync("a", 3);
        secrets.CompletePresenceCall("a", 0, false);
        await vm.RefreshSecretPresenceCommand.ExecutionTask!;

        vm.RefreshSecretPresenceCommand.Execute(null);
        await secrets.WaitForPresenceCallsAsync("a", 6);
        vm.RefreshSecretPresenceCommand.Execute(null);
        await secrets.WaitForPresenceCallsAsync("a", 9);
        secrets.CompletePresenceCall("a", 2, true);
        await vm.RefreshSecretPresenceCommand.ExecutionTask!;
        Assert.True(vm.HasSecret);
        Assert.True(vm.HasDataverseSecret);
        Assert.True(vm.HasDiSecret);

        secrets.CompletePresenceCall("a", 1, false);
        await Task.Yield();

        Assert.True(vm.HasSecret);
        Assert.True(vm.HasDataverseSecret);
        Assert.True(vm.HasDiSecret);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_delete_reports_committed_truth_when_replacement_activation_fails(bool failActivation)
    {
        var store = new ControlledProfileStore(Profile("a", "A"), Profile("b", "B"))
        {
            ActiveError = failActivation ? new InvalidOperationException("default write failed") : null
        };
        using var shell = new ShellViewModel(profileStore: store);
        shell.CurrentTool = shell.Tools.Single(tool => tool.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.Selected = profiles.Profiles.Single(profile => profile.Id == "a");

        await profiles.DeleteProfileCommand.ExecuteAsync(null);

        Assert.DoesNotContain(store.GetAll(), profile => profile.Id == "a");
        Assert.DoesNotContain(profiles.Profiles, profile => profile.Id == "a");
        if (failActivation)
        {
            Assert.Null(shell.ActiveEnvironment);
            Assert.Null(store.ActiveId);
            Assert.Contains("was deleted", profiles.Status);
            Assert.Contains("default write failed", profiles.Status);
        }
        else
        {
            Assert.Equal("b", shell.ActiveEnvironment?.Id);
            Assert.Equal("b", store.ActiveId);
        }
    }

    [Fact]
    public void Environment_write_gate_excludes_profile_commit_and_live_write_in_both_directions()
    {
        var gate = new EnvironmentWriteGate();
        Assert.True(gate.TryAcquireProfileCommit(out var profile));
        Assert.False(gate.TryAcquireProfileCommit(out _));
        Assert.False(gate.TryAcquireLiveWrite(out _));
        profile!.Dispose();

        Assert.True(gate.TryAcquireLiveWrite(out var liveOne));
        Assert.True(gate.TryAcquireLiveWrite(out var liveTwo));
        Assert.False(gate.TryAcquireProfileCommit(out _));
        liveOne!.Dispose();
        Assert.False(gate.TryAcquireProfileCommit(out _));
        liveTwo!.Dispose();
        Assert.True(gate.TryAcquireProfileCommit(out var final));
        final!.Dispose();
    }

    [Fact]
    public async Task Post_dispatch_is_refused_during_profile_commit_and_live_dispatch_blocks_profile_commit()
    {
        var gate = new EnvironmentWriteGate();
        var client = new GatedODataClient();
        using var vm = new PostBuilderViewModel(client, dialogs: new ConfirmYes(), environmentWriteGate: gate);
        Assert.True(gate.TryAcquireProfileCommit(out var profile));

        await vm.SendCommand.ExecuteAsync(null);
        Assert.Equal(0, client.Calls);
        profile!.Dispose();

        var sending = vm.SendCommand.ExecuteAsync(null);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(gate.TryAcquireProfileCommit(out _));
        client.Release.SetResult(new ODataResponse(200, "OK", "{}", 1) { DispatchStarted = true });
        await sending;
        Assert.True(gate.TryAcquireProfileCommit(out var after));
        after!.Dispose();
    }

    [Fact]
    public async Task Operations_dispatch_is_refused_while_profile_commit_owns_the_shared_gate()
    {
        var gate = new EnvironmentWriteGate();
        var connector = new FakeDualWriteConnector();
        var active = Profile("a", "A");
        using var vm = new DualWriteOpsViewModel(
            connector, () => active, new ConfirmYes(), environmentWriteGate: gate);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        Assert.True(gate.TryAcquireProfileCommit(out var profile));

        Assert.False(vm.RunActionCommand.CanExecute(vm.StartAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StartAction);

        Assert.Equal(0, connector.LastGateway!.StartCount);
        profile!.Dispose();
        Assert.True(vm.RunActionCommand.CanExecute(vm.StartAction));
    }

    [Fact]
    public async Task Shell_reenables_live_write_only_after_new_active_identity_is_published()
    {
        var store = new ControlledProfileStore(Profile("a", "A"), Profile("b", "B"));
        using var shell = new ShellViewModel(profileStore: store, dialogs: new ConfirmYes());
        shell.CurrentTool = shell.Tools.Single(tool => tool.Id == "post");
        var oldPost = Assert.IsType<PostBuilderViewModel>(shell.CurrentContent);

        var target = shell.Environments.Single(profile => profile.Id == "b");
        await shell.SetActiveEnvironmentCommand.ExecuteAsync(target);

        Assert.Equal("b", shell.ActiveEnvironment?.Id);
        Assert.False(oldPost.SendCommand.CanExecute(null));
        var currentPost = Assert.IsType<PostBuilderViewModel>(shell.CurrentContent);
        Assert.NotSame(oldPost, currentPost);
        Assert.True(currentPost.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task Shell_disposal_during_cancellation_ignoring_default_write_suppresses_late_ui_publication()
    {
        var store = new ControlledProfileStore(Profile("a", "A"), Profile("b", "B"))
        {
            ActiveRelease = NewGate(),
            IgnoreActiveCancellation = true,
        };
        var shell = new ShellViewModel(profileStore: store);
        var target = shell.Environments.Single(profile => profile.Id == "b");
        var switching = shell.SetActiveEnvironmentCommand.ExecuteAsync(target);
        await store.ActiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        shell.Dispose();
        store.ActiveRelease.SetResult(true);
        await switching;

        Assert.Equal("b", store.ActiveId);
        Assert.Equal("a", shell.ActiveEnvironment?.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_shell_pending_default_failure_or_cancellation_does_not_publish_error(bool fail)
    {
        var store = new ControlledProfileStore(Profile("a", "A"), Profile("b", "B"))
        {
            ActiveRelease = NewGate(),
            IgnoreActiveCancellation = fail,
            ActiveError = fail ? new InvalidOperationException("late default failure") : null,
        };
        var shell = new ShellViewModel(profileStore: store);
        var backgroundChanges = 0;
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.BackgroundError)) backgroundChanges++;
        };
        var target = shell.Environments.Single(profile => profile.Id == "b");
        var switching = shell.SetActiveEnvironmentCommand.ExecuteAsync(target);
        await store.ActiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        shell.Dispose();
        store.ActiveRelease.SetResult(true);
        await switching.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Empty(shell.BackgroundError);
        Assert.Equal(0, backgroundChanges);
        Assert.Equal("a", shell.ActiveEnvironment?.Id);
    }

    [Fact]
    public async Task Disposed_shell_observes_header_cancellation_queued_behind_profile_confirmation()
    {
        var dialogs = new BlockingConfirm();
        var store = new ControlledProfileStore(Profile("a", "A"), Profile("b", "B"));
        var shell = new ShellViewModel(profileStore: store, dialogs: dialogs);
        shell.CurrentTool = shell.Tools.Single(tool => tool.Id == "post");
        shell.CurrentTool = shell.Tools.Single(tool => tool.Id == "profiles");
        var profiles = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        profiles.DraftLegal = "DEMF";
        var saving = profiles.SaveCommand.ExecuteAsync(null);
        await dialogs.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var target = shell.Environments.Single(profile => profile.Id == "b");
        var queuedHeader = shell.SetActiveEnvironmentCommand.ExecuteAsync(target);
        await Task.Yield();
        shell.Dispose();
        dialogs.Release.SetResult(true);
        await Task.WhenAll(saving, queuedHeader).WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Empty(shell.BackgroundError);
        Assert.Equal("a", shell.ActiveEnvironment?.Id);
        Assert.Equal("a", store.ActiveId);
    }

    [Fact]
    public async Task Disposed_post_retains_live_write_lease_until_cancellation_ignoring_transport_drains()
    {
        var gate = new EnvironmentWriteGate();
        var client = new GatedODataClient();
        var vm = new PostBuilderViewModel(client, dialogs: new ConfirmYes(), environmentWriteGate: gate);
        var sending = vm.SendCommand.ExecuteAsync(null);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        vm.Dispose();
        Assert.False(gate.TryAcquireProfileCommit(out _));
        client.Release.SetResult(new ODataResponse(200, "OK", "{}", 1) { DispatchStarted = true });
        await sending;

        Assert.True(gate.TryAcquireProfileCommit(out var after));
        after!.Dispose();
    }

    [Fact]
    public async Task Disposed_operations_retains_live_write_lease_until_cancellation_ignoring_gateway_drains()
    {
        var gate = new EnvironmentWriteGate();
        var connector = new CancellationIgnoringOpsConnector(Profile("a", "A"));
        var vm = new DualWriteOpsViewModel(
            connector, () => connector.Profile, new ConfirmYes(), pollInterval: TimeSpan.Zero,
            environmentWriteGate: gate);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        var running = vm.RunActionCommand.ExecuteAsync(vm.StartAction);
        await connector.Gateway.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        vm.Dispose();
        Assert.False(gate.TryAcquireProfileCommit(out _));
        connector.Gateway.Release.SetResult(true);
        await running;

        Assert.True(gate.TryAcquireProfileCommit(out var after));
        after!.Dispose();
    }

    private static TaskCompletionSource<bool> NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ControlledProfileStore : IProfileStore
    {
        private readonly List<EnvProfile> _profiles;
        public ControlledProfileStore(params EnvProfile[] profiles)
        {
            _profiles = profiles.ToList();
            ActiveId = profiles.FirstOrDefault()?.Id;
        }
        public TaskCompletionSource<bool> SaveStarted { get; } = NewGate();
        public TaskCompletionSource<bool>? SaveRelease { get; set; }
        public TaskCompletionSource<bool> DeleteStarted { get; } = NewGate();
        public TaskCompletionSource<bool>? DeleteRelease { get; set; }
        public TaskCompletionSource<bool> ActiveStarted { get; } = NewGate();
        public TaskCompletionSource<bool>? ActiveRelease { get; set; }
        public int SaveCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int ActiveCalls { get; private set; }
        public Exception? SaveError { get; set; }
        public Exception? ActiveError { get; set; }
        public bool IgnoreActiveCancellation { get; set; }
        public IReadOnlyList<EnvProfile> GetAll() => _profiles.ToArray();
        public void Save(EnvProfile profile) => ApplySave(profile);
        public void Delete(string id) => ApplyDelete(id);
        public string? ActiveId { get; set; }
        public async Task SaveAsync(EnvProfile profile, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            SaveStarted.TrySetResult(true);
            if (SaveRelease is not null) await SaveRelease.Task.WaitAsync(cancellationToken);
            if (SaveError is not null) throw SaveError;
            ApplySave(profile);
        }
        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            DeleteStarted.TrySetResult(true);
            if (DeleteRelease is not null) await DeleteRelease.Task.WaitAsync(cancellationToken);
            ApplyDelete(id);
        }
        public async Task SetActiveAsync(string? id, CancellationToken cancellationToken = default)
        {
            ActiveCalls++;
            ActiveStarted.TrySetResult(true);
            if (ActiveRelease is not null)
            {
                if (IgnoreActiveCancellation) await ActiveRelease.Task;
                else await ActiveRelease.Task.WaitAsync(cancellationToken);
            }
            if (ActiveError is not null) throw ActiveError;
            ActiveId = id;
        }
        private void ApplySave(EnvProfile profile)
        {
            var index = _profiles.FindIndex(item => item.Id == profile.Id);
            if (index < 0) _profiles.Add(profile); else _profiles[index] = profile;
        }
        private void ApplyDelete(string id)
        {
            _profiles.RemoveAll(item => item.Id == id);
            if (ActiveId == id) ActiveId = null;
        }
    }

    private sealed class ControlledSecretStore : ISecretStore
    {
        private readonly Dictionary<(string Id, SecretTarget Target), bool> _values = new();
        private readonly Dictionary<(string Id, SecretTarget Target), TaskCompletionSource<bool>> _presence = new();
        private readonly Dictionary<(string Id, SecretTarget Target), int> _presenceCalls = new();
        private readonly Dictionary<(string Id, SecretTarget Target), List<TaskCompletionSource<bool>>> _presenceHistory = new();
        public bool SetResult { get; set; } = true;
        public TaskCompletionSource<bool>? SetRelease { get; set; }
        public TaskCompletionSource<bool> SetStarted { get; private set; } = NewGate();
        public TaskCompletionSource<bool>? ClearRelease { get; set; }
        public TaskCompletionSource<bool> ClearStarted { get; } = NewGate();
        public int ClearCalls { get; private set; }
        public bool GatePresence { get; set; }
        public Exception? PresenceError { get; set; }
        public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo) =>
            _values.TryGetValue((key, target), out var value) && value;
        public Task<bool> HasSecretAsync(string key, SecretTarget target = SecretTarget.Fo,
            CancellationToken cancellationToken = default)
        {
            if (PresenceError is not null) return Task.FromException<bool>(PresenceError);
            if (!GatePresence) return Task.FromResult(HasSecret(key, target));
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _presence[(key, target)] = gate;
            _presenceCalls[(key, target)] = _presenceCalls.GetValueOrDefault((key, target)) + 1;
            if (!_presenceHistory.TryGetValue((key, target), out var history))
                _presenceHistory[(key, target)] = history = new List<TaskCompletionSource<bool>>();
            history.Add(gate);
            return gate.Task;
        }
        public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo) =>
            _values[(key, target)] = SetResult;
        public async Task<bool> SetSecretAsync(string key, string plaintext, SecretTarget target = SecretTarget.Fo,
            CancellationToken cancellationToken = default)
        {
            SetStarted.TrySetResult(true);
            if (SetRelease is not null) await SetRelease.Task.WaitAsync(cancellationToken);
            if (SetResult) _values[(key, target)] = true;
            return SetResult;
        }
        public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo) => _values[(key, target)] = false;
        public async Task ClearSecretAsync(string key, SecretTarget target = SecretTarget.Fo,
            CancellationToken cancellationToken = default)
        {
            ClearCalls++;
            ClearStarted.TrySetResult(true);
            if (ClearRelease is not null) await ClearRelease.Task.WaitAsync(cancellationToken);
            ClearSecret(key, target);
        }
        public void CompletePresence(string id, bool value, params SecretTarget[] targets)
        {
            var selectedTargets = targets.Length == 0
                ? Enum.GetValues<SecretTarget>()
                : targets;
            foreach (var entry in _presence.Where(item => item.Key.Id == id && selectedTargets.Contains(item.Key.Target)).ToArray())
                entry.Value.TrySetResult(value);
        }
        public void CompletePresenceCall(string id, int callIndex, bool value)
        {
            foreach (var target in Enum.GetValues<SecretTarget>())
                _presenceHistory[(id, target)][callIndex].TrySetResult(value);
        }
        public async Task WaitForPresenceCallsAsync(string id, int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_presenceCalls.Where(item => item.Key.Id == id).Sum(item => item.Value) < count && DateTime.UtcNow < deadline)
                await Task.Yield();
            Assert.True(_presenceCalls.Where(item => item.Key.Id == id).Sum(item => item.Value) >= count);
        }
        public void ResetSetStarted() => SetStarted = NewGate();
    }

    private sealed class GatedODataClient : IODataClient
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = NewGate();
        public TaskCompletionSource<ODataResponse> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ODataResponse> SendAsync(string method, string path, string? body,
            CancellationToken ct = default)
        {
            Calls++;
            Started.TrySetResult(true);
            return Release.Task;
        }
    }

    private sealed class FreshProbeSecretStore : ISecretStore
    {
        private int _reads;
        public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo) => false;
        public Task<bool> HasSecretAsync(string key, SecretTarget target = SecretTarget.Fo,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Interlocked.Increment(ref _reads) > 3);
        }
        public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo) { }
        public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo) { }
    }

    private sealed class CountingConnectionTester : IConnectionTester
    {
        public int FoCalls { get; private set; }
        public Task<ConnectionTestResult> TestFoAsync(EnvProfile env, CancellationToken ct = default)
        {
            FoCalls++;
            return Task.FromResult(new ConnectionTestResult(true, "fresh credential accepted"));
        }
        public Task<ConnectionTestResult> TestDataverseAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult(new ConnectionTestResult(true, "ok"));
    }

    private sealed class CancellationIgnoringOpsConnector : IDualWriteConnector
    {
        public CancellationIgnoringOpsConnector(EnvProfile profile)
        {
            Profile = profile;
            Gateway = new CancellationIgnoringGateway();
        }
        public EnvProfile Profile { get; }
        public CancellationIgnoringGateway Gateway { get; }
        public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult(new DualWriteSession(Gateway, "cid", "connection", env));
    }

    private sealed class CancellationIgnoringGateway : IDualWriteGateway
    {
        private readonly DualWriteMap _map = new(
            "map", "Customers", "Customers", "project", "Stopped", null, Array.Empty<DualWriteTemplate>())
        {
            Actions = new HashSet<string> { DualWriteActionType.Start.ToActionCode() },
        };
        public TaskCompletionSource<bool> Started { get; } = NewGate();
        public TaskCompletionSource<bool> Release { get; } = NewGate();
        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DualWriteEnvironment("cid", "connection", foIdentifier));
        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DualWriteMap>>(new[] { _map });
        public async Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action,
            IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Release.Task; // deliberate: accepted mutation ignores cancellation until transport drains.
            return new DualWriteActionResponse("request", "Submitted");
        }
        public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DualWriteRequestStatus(requestId, "Completed", true, true, null));
        public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId,
            string templateId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DualWriteFieldMapping>>(Array.Empty<DualWriteFieldMapping>());
        public Task RefreshTablesAsync(string fieldMappingName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet,
            IReadOnlyList<string> legalEntities, bool forceReset, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName,
            IReadOnlyList<string> keyFields, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ConfirmYes : IDialogService
    {
        public Task<bool> ConfirmAsync(ConfirmRequest request) => Task.FromResult(true);
    }

    private sealed class BlockingConfirm : IDialogService
    {
        public TaskCompletionSource<bool> Started { get; } = NewGate();
        public TaskCompletionSource<bool> Release { get; } = NewGate();
        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Started.TrySetResult(true);
            return Release.Task;
        }
    }
}
