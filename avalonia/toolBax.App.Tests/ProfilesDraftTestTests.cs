using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Views;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

[CollectionDefinition("Profile probe finalization", DisableParallelization = true)]
public sealed class ProfileProbeFinalizationCollection
{
}

[Collection("Profile probe finalization")]
public sealed class ProfilesDraftTestTests
{
    private static EnvProfile Profile(string id = "a") => new(id, "Saved", "https://saved.example", "tenant", "USMF", "Tier 1", EnvStatus.Connected)
    {
        DataverseUrl = "https://saved.crm.example",
        ClientId = "saved-client",
        DataverseClientId = "saved-dv-client"
    };

    private sealed class Store : IProfileStore
    {
        private readonly List<EnvProfile> _profiles = new() { Profile(), Profile("b") };
        private string? _activeId = "a";
        public Store(EnvProfile? saved = null)
        {
            if (saved is not null) _profiles[0] = saved;
        }
        public int Saves { get; private set; }
        public int Deletes { get; private set; }
        public int ActiveWrites { get; private set; }
        public IReadOnlyList<EnvProfile> GetAll() => _profiles.ToArray();
        public void Save(EnvProfile profile) { Saves++; _profiles[_profiles.FindIndex(p => p.Id == profile.Id)] = profile; }
        public void Delete(string id) { Deletes++; _profiles.RemoveAll(p => p.Id == id); }
        public string? ActiveId { get => _activeId; set { ActiveWrites++; _activeId = value; } }
    }

    private sealed class Secrets : ISecretStore
    {
        public int Writes { get; private set; }
        public bool Present { get; set; } = true;
        public bool ThrowOnRead { get; set; }
        public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo) =>
            ThrowOnRead ? throw new InvalidOperationException("credential lookup unavailable") : Present;
        public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo) => Writes++;
        public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo) => Writes++;
    }

    private sealed class Tester : IConnectionTester, IDualWriteGatewayTester
    {
        public List<(string Kind, EnvProfile Profile, CancellationToken Token)> Calls { get; } = new();
        public Func<string, EnvProfile, CancellationToken, Task<ConnectionTestResult>> Run { get; set; } =
            (_, _, _) => Task.FromResult(new ConnectionTestResult(true, "PROBE_OK"));
        private Task<ConnectionTestResult> Probe(string kind, EnvProfile profile, CancellationToken ct)
        {
            Calls.Add((kind, profile, ct));
            return Run(kind, profile, ct);
        }
        public Task<ConnectionTestResult> TestFoAsync(EnvProfile env, CancellationToken ct = default) => Probe("fo", env, ct);
        public Task<ConnectionTestResult> TestDataverseAsync(EnvProfile env, CancellationToken ct = default) => Probe("dv", env, ct);
        public async Task<DwGatewayTestResult> TestAsync(EnvProfile env, CancellationToken ct = default)
        {
            var result = await Probe("gateway", env, ct);
            return new DwGatewayTestResult(result.Success, result.Message);
        }
    }

    private static IAsyncRelayCommand Command(ProfilesViewModel vm, string kind) => kind switch
    {
        "fo" => vm.TestConnectionCommand,
        "dv" => vm.TestDataverseConnectionCommand,
        _ => vm.TestGatewayCommand
    };

    [Theory]
    [InlineData("fo")]
    [InlineData("dv")]
    [InlineData("gateway")]
    public async Task Test_receives_all_visible_drafts_without_saving_or_overwriting_global_status(string kind)
    {
        var store = new Store();
        var secrets = new Secrets();
        var tester = new Tester();
        var vm = new ProfilesViewModel(store, secrets, gatewayTester: tester, connectionTester: tester);
        var before = store.GetAll().ToArray();
        vm.DraftName = "Draft name";
        vm.DraftUrl = "https://draft.example/data";
        vm.DraftTenant = "draft-tenant";
        vm.DraftDataverseUrl = "https://draft.crm.example/api/data/v9.2";
        vm.DraftClientId = "draft-client";
        vm.DraftDataverseClientId = "draft-dv-client";
        if (kind != "fo") vm.DraftAuthMode = FoAuthMode.ClientSecret;
        if (kind != "dv") vm.DraftDataverseAuthMode = FoAuthMode.ClientSecret;
        vm.Status = "GLOBAL_SAVE_STATUS";
        await Command(vm, kind).ExecuteAsync(null);
        var profile = Assert.Single(tester.Calls).Profile;
        Assert.Equal("Draft name", profile.Name);
        Assert.Equal(vm.DraftUrl, profile.Url);
        Assert.Equal(vm.DraftTenant, profile.Tenant);
        Assert.Equal(vm.DraftDataverseUrl, profile.DataverseUrl);
        Assert.Equal(vm.DraftClientId, profile.ClientId);
        Assert.Equal(vm.DraftDataverseClientId, profile.DataverseClientId);
        Assert.Equal(vm.DraftAuthMode, profile.AuthMode);
        Assert.Equal(vm.DraftDataverseAuthMode, profile.DataverseAuthMode);
        Assert.Equal("GLOBAL_SAVE_STATUS", vm.Status);
        Assert.Equal(before, store.GetAll());
        Assert.Equal("a", store.ActiveId);
        Assert.Equal(0, store.Saves + store.Deletes + store.ActiveWrites + secrets.Writes);
    }

    [Theory]
    [InlineData("fo")]
    [InlineData("dv")]
    public async Task Pending_secret_refuses_before_any_probe(string kind)
    {
        var tester = new Tester();
        var vm = new ProfilesViewModel(new Store(), new Secrets(), connectionTester: tester);
        if (kind == "fo") vm.SecretInput = "pending secret";
        else vm.DataverseSecretInput = "pending secret";
        await Command(vm, kind).ExecuteAsync(null);
        Assert.Empty(tester.Calls);
        Assert.Equal("pending secret", kind == "fo" ? vm.SecretInput : vm.DataverseSecretInput);
        Assert.Contains("Store", TestStatus(vm, kind));
    }

    private static string TestStatus(ProfilesViewModel vm, string kind) => kind switch
    {
        "fo" => vm.FoTestStatus,
        "dv" => vm.DataverseTestStatus,
        _ => vm.DiStatus
    };

    private static bool Busy(ProfilesViewModel vm, string kind) => kind switch
    {
        "fo" => vm.IsTestingFoConnection,
        "dv" => vm.IsTestingDataverseConnection,
        _ => vm.IsTestingGateway
    };

    public static IEnumerable<object[]> DriftCases()
    {
        foreach (var kind in new[] { "fo", "dv", "gateway" })
        foreach (var change in new[] { "name", "url", "auth", "switch", "null", "remove", "delete", "save", "aba" })
        foreach (var fault in new[] { false, true })
            yield return new object[] { kind, change, fault };
    }

    [Theory]
    [MemberData(nameof(DriftCases))]
    public async Task Changed_draft_or_selection_discards_late_success_and_error(string kind, string change, bool fault)
    {
        var gate = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tester = new Tester { Run = (_, _, _) => gate.Task };
        var store = new Store();
        var secrets = new Secrets();
        using var vm = new ProfilesViewModel(store, secrets, gatewayTester: tester, connectionTester: tester);
        var originalUrl = vm.DraftUrl;
        var operation = Command(vm, kind).ExecuteAsync(null);
        Assert.Single(tester.Calls);
        switch (change)
        {
            case "name": vm.DraftName = "Changed name"; break;
            case "url": vm.DraftUrl = "https://changed.example"; break;
            case "auth": vm.DraftTenant = "changed-tenant"; break;
            case "switch": vm.Selected = vm.Profiles[1]; break;
            case "null": vm.Selected = null; break;
            case "remove": vm.Profiles.Remove(vm.Selected!); break;
            case "delete": vm.DeleteProfileCommand.Execute(null); break;
            case "save": await vm.SaveCommand.ExecuteAsync(null); break;
            case "aba": vm.DraftUrl = "https://changed.example"; vm.DraftUrl = originalUrl; break;
        }
        var global = vm.Status;
        var writes = store.Saves + store.Deletes + store.ActiveWrites + secrets.Writes;
        if (fault) gate.SetException(new InvalidOperationException("OLD_RESULT"));
        else gate.SetResult(new ConnectionTestResult(true, "OLD_RESULT"));
        await operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(tester.Calls[0].Token.IsCancellationRequested);
        Assert.Equal(string.Empty, TestStatus(vm, kind));
        Assert.Equal(global, vm.Status);
        Assert.Equal(writes, store.Saves + store.Deletes + store.ActiveWrites + secrets.Writes);
        Assert.False(Busy(vm, kind));
    }

    [Theory]
    [InlineData("fo", "success")]
    [InlineData("fo", "failure")]
    [InlineData("fo", "cancel")]
    [InlineData("dv", "success")]
    [InlineData("dv", "failure")]
    [InlineData("dv", "cancel")]
    [InlineData("gateway", "success")]
    [InlineData("gateway", "failure")]
    [InlineData("gateway", "cancel")]
    public async Task Tests_never_persist_profile_active_or_secret_state(string kind, string outcome)
    {
        var gate = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tester = new Tester { Run = (_, _, _) => gate.Task };
        var store = new Store();
        var secrets = new Secrets();
        var before = store.GetAll().ToArray();
        using var vm = new ProfilesViewModel(store, secrets, gatewayTester: tester, connectionTester: tester);
        vm.Status = "SAVE_STATUS";
        var command = Command(vm, kind);
        var operation = command.ExecuteAsync(null);
        if (outcome == "cancel") command.Cancel();
        if (outcome == "failure") gate.SetException(new InvalidOperationException("PROBE_FAILURE"));
        else gate.SetResult(new ConnectionTestResult(true, "PROBE_OK"));
        await operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(before, store.GetAll());
        Assert.Equal("a", store.ActiveId);
        Assert.True(secrets.Present);
        Assert.Equal(0, store.Saves + store.Deletes + store.ActiveWrites + secrets.Writes);
        Assert.Equal("SAVE_STATUS", vm.Status);
        Assert.Contains("Saved", TestStatus(vm, kind));
        Assert.Contains(kind == "dv" ? "saved.crm.example" : "saved.example", TestStatus(vm, kind));
    }

    [Theory]
    [InlineData("fo")]
    [InlineData("dv")]
    [InlineData("gateway")]
    public async Task Direct_overlap_cannot_replace_the_accepted_token_task_or_busy_owner(string kind)
    {
        var tester = new Tester
        {
            Run = async (_, _, ct) =>
            {
                await new TaskCompletionSource().Task.WaitAsync(ct);
                return new ConnectionTestResult(true, "unreachable");
            }
        };
        using var vm = new ProfilesViewModel(new Store(), new Secrets(), gatewayTester: tester, connectionTester: tester);
        var command = Command(vm, kind);
        var operation = command.ExecuteAsync(null);
        await command.ExecuteAsync(null);
        Assert.Same(operation, command.ExecutionTask);
        Assert.Single(tester.Calls);
        Assert.True(command.CanBeCanceled);
        Assert.True(Busy(vm, kind));
        command.Cancel();
        await operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(tester.Calls[0].Token.IsCancellationRequested);
        Assert.Contains("cancelled", TestStatus(vm, kind));
        Assert.False(Busy(vm, kind));
    }

    [Fact]
    public async Task Independent_targets_keep_their_status_and_busy_state_separate()
    {
        var gates = new Dictionary<string, TaskCompletionSource<ConnectionTestResult>>
        {
            ["fo"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["dv"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["gateway"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var tester = new Tester { Run = (kind, _, _) => gates[kind].Task };
        using var vm = new ProfilesViewModel(new Store(), new Secrets(), gatewayTester: tester, connectionTester: tester);
        vm.Status = "SAVE_STATUS";
        var fo = vm.TestConnectionCommand.ExecuteAsync(null);
        var dv = vm.TestDataverseConnectionCommand.ExecuteAsync(null);
        var gateway = vm.TestGatewayCommand.ExecuteAsync(null);
        gates["dv"].SetResult(new ConnectionTestResult(true, "DV_ONLY"));
        await dv;
        Assert.Contains("DV_ONLY", vm.DataverseTestStatus);
        Assert.True(vm.IsTestingFoConnection);
        Assert.True(vm.IsTestingGateway);
        gates["fo"].SetException(new InvalidOperationException("FO_ONLY"));
        await fo;
        Assert.Contains("FO_ONLY", vm.FoTestStatus);
        Assert.Contains("DV_ONLY", vm.DataverseTestStatus);
        vm.TestGatewayCommand.Cancel();
        await gateway.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gates["gateway"].SetResult(new ConnectionTestResult(true, "LATE_GATEWAY"));
        Assert.Contains("cancelled", vm.DiStatus);
        Assert.Equal("SAVE_STATUS", vm.Status);
    }

    [Theory]
    [InlineData("fo", "client")]
    [InlineData("fo", "tenant")]
    [InlineData("fo", "unsupported")]
    [InlineData("dv", "client")]
    [InlineData("dv", "tenant")]
    [InlineData("dv", "unsupported")]
    public async Task Client_secret_or_unsupported_draft_refuses_before_probe(string kind, string change)
    {
        var saved = Profile() with { AuthMode = FoAuthMode.ClientSecret, DataverseAuthMode = FoAuthMode.ClientSecret };
        var tester = new Tester();
        var store = new Store(saved);
        var secrets = new Secrets();
        using var vm = new ProfilesViewModel(store, secrets, connectionTester: tester);
        if (change == "tenant") vm.DraftTenant = "different";
        else if (change == "client")
        {
            if (kind == "fo") vm.DraftClientId = "different";
            else vm.DraftDataverseClientId = "different";
        }
        else
        {
            if (kind == "fo") vm.DraftAuthMode = FoAuthMode.Certificate;
            else vm.DraftDataverseAuthMode = FoAuthMode.Certificate;
        }
        await Command(vm, kind).ExecuteAsync(null);
        Assert.Empty(tester.Calls);
        Assert.Contains(change == "unsupported" ? "unsupported" : "Store", TestStatus(vm, kind));
        Assert.Equal(0, store.Saves + secrets.Writes);
    }

    [Theory]
    [InlineData("fo", true)]
    [InlineData("dv", true)]
    [InlineData("fo", false)]
    [InlineData("dv", false)]
    public async Task Name_and_url_drafts_reuse_only_matching_stored_credentials(string kind, bool secretPresent)
    {
        var saved = Profile() with { AuthMode = FoAuthMode.ClientSecret, DataverseAuthMode = FoAuthMode.ClientSecret };
        var tester = new Tester();
        var store = new Store(saved);
        var secrets = new Secrets { Present = secretPresent };
        using var vm = new ProfilesViewModel(store, secrets, connectionTester: tester);
        vm.DraftName = "Draft target";
        vm.DraftUrl = "https://draft.example";
        vm.DraftDataverseUrl = "https://draft.crm.example";
        await Command(vm, kind).ExecuteAsync(null);
        if (secretPresent)
        {
            Assert.Equal("Draft target", Assert.Single(tester.Calls).Profile.Name);
            Assert.Contains("Draft target", TestStatus(vm, kind));
        }
        else
        {
            Assert.Empty(tester.Calls);
            Assert.Contains("Store", TestStatus(vm, kind));
        }
        Assert.Equal(0, store.Saves + secrets.Writes);
    }

    [Fact]
    public async Task Completed_results_are_invalidated_by_the_next_draft_edit()
    {
        var tester = new Tester();
        using var vm = new ProfilesViewModel(new Store(), gatewayTester: tester, connectionTester: tester);
        await vm.TestConnectionCommand.ExecuteAsync(null);
        await vm.TestDataverseConnectionCommand.ExecuteAsync(null);
        await vm.TestGatewayCommand.ExecuteAsync(null);
        Assert.Contains("Connected", vm.FoTestStatus);
        Assert.Contains("Connected", vm.DataverseTestStatus);
        Assert.Contains("Connected", vm.DiStatus);
        vm.DraftName = "Edited after success";
        Assert.Empty(vm.FoTestStatus);
        Assert.Empty(vm.DataverseTestStatus);
        Assert.Empty(vm.DiStatus);
    }

    [Fact]
    public async Task Draft_edit_invalidates_gateway_result_without_deleting_legacy_clear_confirmation()
    {
        var tester = new Tester();
        using var vm = new ProfilesViewModel(new Store(), new Secrets(), gatewayTester: tester,
            connectionTester: tester);
        vm.ClearLegacyDiPasswordCommand.Execute(null);
        Assert.NotEmpty(vm.LegacyDiStatus);

        await vm.TestGatewayCommand.ExecuteAsync(null);
        Assert.Contains("PROBE_OK", vm.DiStatus);

        vm.DraftName = "Edited after gateway success";

        Assert.Empty(vm.DiStatus);
        Assert.Equal("Legacy Data Integrator password cleared.", vm.LegacyDiStatus);
    }

    [Fact]
    public async Task Dispose_cancels_all_probes_and_late_responses_cannot_publish()
    {
        var gate = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tester = new Tester { Run = (_, _, _) => gate.Task };
        var vm = new ProfilesViewModel(new Store(), gatewayTester: tester, connectionTester: tester);
        var pending = new[] { vm.TestConnectionCommand.ExecuteAsync(null), vm.TestDataverseConnectionCommand.ExecuteAsync(null), vm.TestGatewayCommand.ExecuteAsync(null) };
        vm.Dispose();
        vm.Dispose();
        gate.SetResult(new ConnectionTestResult(true, "LATE_RESULT"));
        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.All(tester.Calls, call => Assert.True(call.Token.IsCancellationRequested));
        Assert.Empty(vm.FoTestStatus);
        Assert.Empty(vm.DataverseTestStatus);
        Assert.Empty(vm.DiStatus);
        Assert.False(vm.TestConnectionCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Closing_real_main_window_cancels_cached_profile_probe_without_late_result()
    {
        var gate = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tester = new Tester { Run = (_, _, _) => gate.Task };
        var shell = new ShellViewModel(profileStore: new Store(), connectionTester: tester);
        shell.CurrentTool = shell.Tools.Single(t => t.Id == "profiles");
        var vm = Assert.IsType<ProfilesViewModel>(shell.CurrentContent);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var pending = vm.TestConnectionCommand.ExecuteAsync(null);
        window.Close();
        shell.Dispose();
        gate.SetResult(new ConnectionTestResult(true, "LATE_CLOSE"));
        await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(tester.Calls).Token.IsCancellationRequested);
        Assert.Empty(vm.FoTestStatus);
        Assert.False(vm.TestConnectionCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData("fo", 0, "FoTestResult", "FoTestCancelButton")]
    [InlineData("dv", 1, "DataverseTestResult", "DataverseTestCancelButton")]
    [InlineData("gateway", 2, "GatewayTestResult", "GatewayTestCancelButton")]
    public async Task Attached_target_status_attributes_the_draft_and_exposes_owner_cancel(string kind, int tab, string resultName, string cancelName)
    {
        var gate = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tester = new Tester { Run = (_, _, _) => gate.Task };
        using var vm = new ProfilesViewModel(new Store(), gatewayTester: tester, connectionTester: tester);
        vm.DraftName = "Visible draft";
        var view = new ProfilesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 950 };
        window.Show();
        try
        {
            view.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = tab;
            Dispatcher.UIThread.RunJobs();
            var operation = Command(vm, kind).ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var cancel = view.FindControl<Button>(cancelName)!;
            Assert.True(cancel.IsEffectivelyVisible);
            Assert.True(cancel.IsEnabled);
            cancel.Command!.Execute(null);
            await operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            gate.SetResult(new ConnectionTestResult(true, "LATE_RENDER"));
            Dispatcher.UIThread.RunJobs();
            var text = view.FindControl<TextBlock>(resultName)!;
            Assert.True(text.IsEffectivelyVisible && text.Bounds.Height > 0);
            Assert.Contains("Visible draft", text.Text);
            Assert.Contains("cancelled", text.Text);
            Assert.DoesNotContain("LATE_RENDER", text.Text);
        }
        finally { gate.TrySetResult(new ConnectionTestResult(true, "cleanup")); window.Close(); }
    }

    [Fact]
    public async Task Draft_probe_and_explicit_save_use_identical_null_and_default_field_handling()
    {
        var tester = new Tester();
        var store = new Store();
        using var vm = new ProfilesViewModel(store, connectionTester: tester);
        vm.DraftName = "Visible";
        vm.DraftUrl = "https://draft.example/data/";
        vm.DraftDataverseUrl = " ";
        vm.DraftClientId = "";
        vm.DraftDataverseClientId = " ";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        var snapshot = Assert.Single(tester.Calls).Profile;
        Assert.Null(snapshot.ClientId);
        Assert.Null(snapshot.DataverseClientId);
        Assert.Null(snapshot.DataverseUrl);
        Assert.Equal(0, store.Saves);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(snapshot, store.GetAll().Single(p => p.Id == snapshot.Id));
        Assert.Equal(1, store.Saves);
        Assert.Empty(vm.FoTestStatus);
    }

    [Theory]
    [InlineData("fo")]
    [InlineData("dv")]
    public async Task Interactive_draft_can_test_changed_auth_without_a_stored_principal(string kind)
    {
        var saved = Profile() with { AuthMode = FoAuthMode.ClientSecret, DataverseAuthMode = FoAuthMode.ClientSecret };
        var tester = new Tester();
        var store = new Store(saved);
        var secrets = new Secrets { Present = false };
        using var vm = new ProfilesViewModel(store, secrets, connectionTester: tester);
        vm.DraftTenant = "new-interactive-tenant";
        if (kind == "fo")
        {
            vm.DraftAuthMode = FoAuthMode.Interactive;
            vm.DraftClientId = "interactive-client";
        }
        else
        {
            vm.DraftDataverseAuthMode = FoAuthMode.Interactive;
            vm.DraftDataverseClientId = "interactive-client";
        }
        await Command(vm, kind).ExecuteAsync(null);
        Assert.Equal("new-interactive-tenant", Assert.Single(tester.Calls).Profile.Tenant);
        Assert.Contains("Connected", TestStatus(vm, kind));
        Assert.Equal(0, store.Saves + secrets.Writes);
    }

    [Fact]
    public async Task Gateway_portal_probe_does_not_consume_fo_or_dataverse_secret_entries()
    {
        var tester = new Tester();
        var secrets = new Secrets();
        using var vm = new ProfilesViewModel(new Store(), secrets, gatewayTester: tester);
        vm.SecretInput = "new-fo-secret";
        vm.DataverseSecretInput = "new-dv-secret";
        await vm.TestGatewayCommand.ExecuteAsync(null);
        Assert.Single(tester.Calls);
        Assert.Equal("new-fo-secret", vm.SecretInput);
        Assert.Equal("new-dv-secret", vm.DataverseSecretInput);
        Assert.Equal(0, secrets.Writes);
    }

    [Theory]
    [InlineData("fo")]
    [InlineData("dv")]
    public async Task Stored_credential_lookup_failure_is_a_local_test_result_not_a_command_fault(string kind)
    {
        var tester = new Tester();
        var store = new Store(Profile() with { AuthMode = FoAuthMode.ClientSecret, DataverseAuthMode = FoAuthMode.ClientSecret });
        var secrets = new Secrets { ThrowOnRead = true };
        using var vm = new ProfilesViewModel(store, secrets, connectionTester: tester);
        await Command(vm, kind).ExecuteAsync(null);
        Assert.Empty(tester.Calls);
        Assert.Contains("credential lookup unavailable", TestStatus(vm, kind));
        Assert.False(Busy(vm, kind));
        Assert.Equal(0, store.Saves + secrets.Writes);
    }

    [Theory]
    [InlineData("fo")]
    [InlineData("dv")]
    [InlineData("gateway")]
    public async Task Invalid_draft_url_text_does_not_fault_current_scope_validation(string kind)
    {
        var tester = new Tester { Run = (_, _, _) => Task.FromResult(new ConnectionTestResult(false, "invalid draft URL")) };
        using var vm = new ProfilesViewModel(new Store(), gatewayTester: tester, connectionTester: tester);
        vm.DraftUrl = "https://[";
        vm.DraftDataverseUrl = "https://[";
        await Command(vm, kind).ExecuteAsync(null);
        Assert.Single(tester.Calls);
        Assert.Contains("invalid draft URL", TestStatus(vm, kind));
        Assert.False(Busy(vm, kind));
    }

    private sealed class AbandonedProbeTester : IConnectionTester, IDualWriteGatewayTester
    {
        private readonly TaskCompletionSource<ConnectionTestResult> _connection =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DwGatewayTestResult> _gateway =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Original(string kind) => kind == "gateway" ? _gateway.Task : _connection.Task;
        public void Fault(string kind, string canary)
        {
            var error = new InvalidOperationException(canary);
            if (kind == "gateway") _gateway.SetException(error);
            else _connection.SetException(error);
        }
        public Task<ConnectionTestResult> TestFoAsync(EnvProfile env, CancellationToken ct = default) => _connection.Task;
        public Task<ConnectionTestResult> TestDataverseAsync(EnvProfile env, CancellationToken ct = default) => _connection.Task;
        public Task<DwGatewayTestResult> TestAsync(EnvProfile env, CancellationToken ct = default) => _gateway.Task;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference UnobservedControl(string canary)
    {
        var source = new TaskCompletionSource();
        source.SetException(new InvalidOperationException(canary));
        return new WeakReference(source.Task);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CancelThenAbandonFaultingProbe(string kind, string canary)
    {
        var tester = new AbandonedProbeTester();
        using var vm = new ProfilesViewModel(new Store(), connectionTester: tester, gatewayTester: tester);
        var command = Command(vm, kind);
        var accepted = command.ExecuteAsync(null);
        command.Cancel();
        await accepted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(tester.Original(kind).IsCompleted); // prompt cancellation, before the ignored probe settles
        var cancelledStatus = TestStatus(vm, kind);
        Assert.Contains("cancelled", cancelledStatus);
        var weak = new WeakReference(tester.Original(kind));
        tester.Fault(kind, canary);
        // Wait for completion without observing the antecedent's exception. This continuation reads no result.
        await tester.Original(kind).ContinueWith(static _ => { }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.Equal(cancelledStatus, TestStatus(vm, kind));
        return weak;
    }

    [Theory]
    [InlineData("fo")]
    [InlineData("dv")]
    [InlineData("gateway")]
    public async Task Cancelled_probe_late_fault_is_observed_without_global_error_publication(string kind)
    {
        var canary = "h08a-late-probe-" + Guid.NewGuid().ToString("N");
        var controlCanary = canary + "-control";
        var lateFaults = 0;
        var controlFaults = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, args) =>
        {
            foreach (var error in args.Exception.Flatten().InnerExceptions)
            {
                if (error.Message == canary) Interlocked.Increment(ref lateFaults);
                else if (error.Message == controlCanary) Interlocked.Increment(ref controlFaults);
                else continue;
                args.SetObserved();
            }
        };
        TaskScheduler.UnobservedTaskException += onUnobserved;
        try
        {
            var probe = await CancelThenAbandonFaultingProbe(kind, canary);
            var control = UnobservedControl(controlCanary);
            // Bounded collection cycles; a collected positive control proves this exercised finalization.
            for (var attempt = 0; attempt < 12 && (probe.IsAlive || control.IsAlive); attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Yield();
            }
            Assert.False(control.IsAlive);
            Assert.False(probe.IsAlive);
            Assert.Equal(1, Volatile.Read(ref controlFaults));
            Assert.Equal(0, Volatile.Read(ref lateFaults));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= onUnobserved;
        }
    }
}
