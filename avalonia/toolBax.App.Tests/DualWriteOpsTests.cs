using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Operations: connect + list real maps (read-path) and run lifecycle actions (confirm → submit →
/// poll to terminal → refresh). Pure VM logic over a fake connector/gateway — fast, no view, no network.
/// </summary>
public class DualWriteOpsTests
{
    private static EnvProfile Env() =>
        new("env1", "Contoso", "https://contoso.operations.dynamics.com", "tenant", "AUMF", "Tier 2", EnvStatus.Connected);

    private static EnvProfile OtherEnv() =>
        new("env2", "Fabrikam", "https://fabrikam.operations.dynamics.com", "tenant", "DEMF", "Tier 2", EnvStatus.Connected);

    private static DualWriteOpsViewModel MakeVm(IDualWriteConnector connector, bool confirm = false) =>
        new(connector, Env, new FakeDialogs(confirm),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));

    /// <summary>A mutable active-environment source: models the shell switching the active environment
    /// under this cached VM (the user having declined the "Refresh open tools?" prompt).</summary>
    private sealed class EnvSwitch
    {
        public EnvProfile? Current { get; set; } = Env();
        public EnvProfile? Get() => Current;
    }

    private sealed class FakeDialogs : IDialogService
    {
        private readonly bool _confirm;
        public FakeDialogs(bool confirm) => _confirm = confirm;
        public int Calls { get; private set; }

        /// <summary>The last request shown — the dialog is what the user reads before agreeing, so what it
        /// lists (and doesn't) is assertable.</summary>
        public ConfirmRequest? LastRequest { get; private set; }

        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(_confirm);
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

    private sealed class ThrowOnceDialogs : IDialogService
    {
        private int _calls;
        public Task<bool> ConfirmAsync(ConfirmRequest request) =>
            Interlocked.Increment(ref _calls) == 1
                ? Task.FromException<bool>(new InvalidOperationException("dialog failed"))
                : Task.FromResult(true);
    }

    private sealed class DeclineOnceDialogs : IDialogService
    {
        private int _calls;
        public Task<bool> ConfirmAsync(ConfirmRequest request) =>
            Task.FromResult(Interlocked.Increment(ref _calls) != 1);
    }

    private sealed class ControlledDebugDialogs : IDialogService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ConfirmRequest> Requests { get; } = new();
        public Task<bool> ConfirmAsync(ConfirmRequest request)
        {
            Requests.Add(request);
            Entered.TrySetResult();
            return Requests.Count == 1 ? Answer.Task : Task.FromResult(true);
        }
    }

    private static async Task WaitForDebugConfirmation(Task operation, ControlledDebugDialogs dialogs)
    {
        // Baseline finishes without asking; fail promptly rather than waiting forever for a missing dialog.
        await Task.WhenAny(operation, dialogs.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(dialogs.Entered.Task.IsCompleted, "Debug operation did not request confirmation.");
    }

    private sealed class CountingConnector : IDualWriteConnector
    {
        private readonly FakeDualWriteConnector _inner = new();
        public int Calls { get; private set; }
        public FakeCoreDualWriteGateway? LastGateway => _inner.LastGateway;

        public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
        {
            Calls++;
            return _inner.ConnectAsync(env, ct);
        }
    }

    // --- read-path ---

    [Fact]
    public async Task Load_connects_and_lists_the_real_maps()
    {
        var vm = MakeVm(new FakeDualWriteConnector());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.True(vm.HasMaps);
        Assert.Equal(FakeDualWriteConnector.SeedMaps().Count, vm.Maps.Count);
        Assert.Equal("Contoso (AUMF · APAC Prod)", vm.ConnectionName);
        Assert.Null(vm.LoadError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Map_rows_project_the_real_dualwrite_fields()
    {
        var vm = MakeVm(new FakeDualWriteConnector());

        await vm.LoadCommand.ExecuteAsync(null);

        var customers = vm.Maps.Single(m => m.Name == "Customers V3");
        Assert.Equal("account", customers.CeEntity);
        Assert.Equal("1.0.0.12", customers.Version);
        Assert.Equal("Microsoft", customers.Author);
        Assert.Equal("Running", customers.State);
    }

    [Fact]
    public async Task Load_with_no_active_environment_reports_an_error()
    {
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), () => null, new FakeDialogs(false));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.False(vm.HasMaps);
        Assert.Contains("environment", vm.LoadError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_surfaces_a_connect_failure_and_stays_disconnected()
    {
        var vm = MakeVm(FakeDualWriteConnector.ThatFails("gateway unreachable"));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("gateway unreachable", vm.LoadError);
        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Maps);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Cancelled_load_resets_to_a_clean_disconnected_state()
    {
        // A genuine user cancel: the connect is in flight when the Cancel command fires, so the VM's own
        // token is cancelled — that, and only that, may be reported as "Cancelled."
        var gate = new TaskCompletionSource();
        var vm = MakeVm(FakeDualWriteConnector.ThatCancelsWhen(gate.Task));

        var running = vm.LoadCommand.ExecuteAsync(null);
        vm.LoadCancelCommand.Execute(null);
        gate.SetResult();
        await running;

        Assert.False(vm.IsConnected);
        Assert.Null(vm.ConnectionName);
        Assert.Empty(vm.Maps);
        Assert.Contains("Cancel", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.LoadError);   // the user asked for this; it isn't an error
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Visible_stop_waiting_cancels_the_accepted_connect_and_discards_a_late_session()
    {
        var connector = new GatedDisposableConnector();
        var vm = MakeVm(connector);

        var load = vm.LoadCommand.ExecuteAsync(null);
        await connector.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        vm.RunActionCancelCommand.Execute(null);
        connector.Release(Env()); // models a provider that completes successfully despite cancellation
        await load.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(connector.ConnectToken.IsCancellationRequested);
        Assert.True(connector.Gateway.Disposed);
        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Maps);
        Assert.Contains("cancel", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    // #166: an HttpClient timeout surfaces as an OperationCanceledException with the caller's token still
    // live. Reporting that as "Cancelled." with no error banner told the user they'd done it themselves.
    [Fact]
    public async Task A_timed_out_load_is_reported_as_a_timeout_not_a_cancel()
    {
        var vm = MakeVm(FakeDualWriteConnector.ThatTimesOut());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("timed out", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(vm.LoadError);
        Assert.Contains("timed out", vm.LoadError!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Err);
        Assert.False(vm.IsConnected);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Load_with_no_maps_shows_the_empty_state()
    {
        var vm = MakeVm(new FakeDualWriteConnector(Array.Empty<DualWriteMap>()));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.False(vm.HasMaps);
        Assert.Contains("No maps", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    // --- lifecycle actions ---

    [Fact]
    public async Task An_action_is_disabled_until_a_map_is_selected()
    {
        var vm = MakeVm(new FakeDualWriteConnector());
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.RunActionCommand.CanExecute(vm.StopAction)); // nothing selected

        vm.Maps.First().IsSelected = true;
        Assert.True(vm.RunActionCommand.CanExecute(vm.StopAction));
    }

    [Fact]
    public async Task Confirmed_action_submits_polls_and_refreshes_the_state()
    {
        var connector = new FakeDualWriteConnector();
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(1, connector.LastGateway!.StartCount);
        var customers = vm.Maps.Single(m => m.Name == "Customers V3");
        Assert.Equal("Stopped", customers.State);   // refreshed to the new state
        Assert.True(customers.IsSelected);            // selection preserved across the refresh
        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Cancelled_confirm_does_not_submit_anything()
    {
        var connector = new FakeDualWriteConnector();
        var vm = MakeVm(connector, confirm: false);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(0, connector.LastGateway!.StartCount);                  // no gateway mutation
        Assert.Equal("Running", vm.Maps.Single(m => m.Name == "Customers V3").State); // unchanged
    }

    [Fact]
    public async Task A_refresh_failure_does_not_clobber_a_successful_action()
    {
        // Load = GetMaps call 1 (ok); the post-action refresh = call 2 (throws).
        var connector = new FakeDualWriteConnector(failGetMapsOnCall: 2);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase); // action result stands
        Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_action_polls_until_the_request_is_terminal()
    {
        var connector = new FakeDualWriteConnector(pollsBeforeTerminal: 3);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        // Start goes to a Stopped map: the seeds' Running maps report the live capture's action list
        // ("4","5" = Stop/Pause), which does NOT include Start, so this used to lean on the screen offering
        // every action on every map (#168). The polling seam under test is unchanged either way.
        vm.Maps.Single(m => m.Name == "Chart of accounts").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StartAction);

        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Mutation_flag_covers_the_confirmation_phase()
    {
        var connector = new FakeDualWriteConnector();
        var dialogs = new GatedDialogs();
        var vm = new DualWriteOpsViewModel(connector, Env, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        var action = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        await dialogs.Entered;
        Assert.True(vm.MutationInProgress);

        dialogs.Release(false);
        await action;

        Assert.False(vm.MutationInProgress);
        Assert.Equal(0, connector.LastGateway!.StartCount);
    }

    [Fact]
    public async Task Held_lifecycle_confirmation_refuses_debug_and_reconnect_without_releasing_the_owner()
    {
        var connector = new CountingConnector();
        var dialogs = new GatedDialogs();
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(connector, Env, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        var lifecycle = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        await dialogs.Entered;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await vm.DisableDebugForSelectedCommand.ExecuteAsync(null);
        await vm.LoadCommand.ExecuteAsync(null);
        var stillOwned = vm.MutationInProgress;
        var stillBusy = vm.IsBusy;
        var metadataCalls = metadata.Calls;
        var httpCalls = odata.Calls.Count;
        var connectorCalls = connector.Calls;

        dialogs.Release(false);
        await lifecycle;

        Assert.True(stillOwned);
        Assert.True(stillBusy);
        Assert.Equal(0, metadataCalls);
        Assert.Equal(0, httpCalls);
        Assert.Equal(1, connectorCalls);
        Assert.False(vm.MutationInProgress);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Held_debug_toggle_refuses_lifecycle_without_opening_confirmation_or_releasing_the_owner()
    {
        var connector = new FakeDualWriteConnector();
        var dialogs = new FakeDialogs(confirm: true);
        var odata = new GatedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(connector, Env, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        var debug = vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await odata.Entered;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        var stillOwned = vm.MutationInProgress;
        var stillBusy = vm.IsBusy;

        odata.Release();
        await debug;

        Assert.Equal(1, dialogs.Calls); // the owning debug operation's confirmation only
        Assert.Equal(0, connector.LastGateway!.StartCount);
        Assert.True(stillOwned);
        Assert.True(stillBusy);
        Assert.False(vm.MutationInProgress);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Declined_or_failed_lifecycle_confirmation_releases_the_gate_for_a_later_debug_toggle()
    {
        foreach (var dialogs in new IDialogService[] { new DeclineOnceDialogs(), new ThrowOnceDialogs() })
        {
            var connector = new FakeDualWriteConnector();
            var odata = new ScriptedODataClient(DebugRecord);
            var vm = new DualWriteOpsViewModel(connector, Env, dialogs,
                pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
                odata: odata, metadata: new FixedMetadata("DualWriteProjectConfigurations"));
            await vm.LoadCommand.ExecuteAsync(null);
            vm.Maps.First().IsSelected = true;

            try
            {
                await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
            }
            catch (InvalidOperationException ex) when (ex.Message == "dialog failed")
            {
                // The command propagates dialog failure; the lease still has to be released in finally.
            }

            Assert.True(vm.EnableDebugForSelectedCommand.CanExecute(null));
            await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

            Assert.Contains(odata.Calls, c => c.Method == "PATCH");
            Assert.False(vm.MutationInProgress);
            Assert.False(vm.IsBusy);
        }
    }

    // --- #168: the gateway's per-map action eligibility (detail.actions) ---

    /// <summary>A map reporting exactly the actions the gateway says its state accepts. No
    /// <paramref name="actions"/> at all = the gateway didn't report any (unknown), as older gateways and
    /// the live capture's Stopped map do.</summary>
    private static DualWriteMap MapWith(string name, string state, params DualWriteActionType[] actions)
    {
        var template = new DualWriteTemplate($"tpl-{name}", "1.0.0.0", "Microsoft");
        return new DualWriteMap($"map-{name}", name, name, $"proj-{name}", state, template, new[] { template })
        {
            RightEntityName = name.ToLowerInvariant(),
            Actions = actions.Length == 0
                ? null
                : actions.Select(a => a.ToActionCode()).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    // A mixed selection — the normal case, since the grid is multi-select and map states differ.
    private static IReadOnlyList<DualWriteMap> MixedEligibilityMaps() => new[]
    {
        MapWith("Customers V3", "Running", DualWriteActionType.Stop, DualWriteActionType.Pause), // Stop: yes
        MapWith("Released products", "Paused", DualWriteActionType.Resume),                      // Stop: no
        MapWith("Vendors V2", "Running"),                                                        // unreported
    };

    [Fact]
    public async Task An_action_sends_only_the_maps_the_gateway_says_can_take_it()
    {
        // MapActionPayloadBuilder batches the whole selection into ONE details[], so including a map the
        // gateway rejects failed the action for every map selected with it — with only an opaque "500" to
        // show for it. The ineligible map must be left out of the payload instead.
        var connector = new FakeDualWriteConnector(MixedEligibilityMaps());
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        foreach (var row in vm.Maps)
        {
            row.IsSelected = true;
        }

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        var gateway = connector.LastGateway!;
        Assert.Equal(1, gateway.StartCount);
        // Sent: the map that reports Stop, plus the one whose actions the gateway never reported.
        Assert.Equal(
            new[] { "Customers V3", "Vendors V2" },
            gateway.LastActionMaps.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
        // The Paused map was never sent — had it been, the fake would have moved it to Stopped.
        Assert.Equal("Paused", vm.Maps.Single(m => m.Name == "Released products").State);
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Vendors V2").State);
        // The action result stands AND the skip is named — a silent exclusion is its own bug.
        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Skipped 1 map(s)", vm.Status);
        Assert.Contains("Stop", vm.Status);
        Assert.Contains("Released products", vm.Status);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn && e.Text.Contains("Released products"));
        // The user selected the skipped map too; nothing was done to it, so it stays selected.
        Assert.True(vm.Maps.Single(m => m.Name == "Released products").IsSelected);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task An_action_no_selected_map_supports_is_refused_before_the_confirm_dialog()
    {
        // Nothing would be left in the payload, so there is nothing to confirm — and sending an empty
        // details[] (or the ineligible maps anyway) could only come back as an opaque failure.
        var connector = new FakeDualWriteConnector(MixedEligibilityMaps());
        var dialogs = new FakeDialogs(confirm: true);
        var vm = new DualWriteOpsViewModel(connector, Env, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        // Both report their actions, and neither list includes Resume.
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;
        vm.Maps.Single(m => m.Name == "Released products").IsSelected = false;
        vm.Maps.Single(m => m.Name == "Vendors V2").IsSelected = false;

        await vm.RunActionCommand.ExecuteAsync(vm.ResumeAction);

        Assert.Equal(0, dialogs.Calls);                     // refused before the dialog, not after it
        Assert.Equal(0, connector.LastGateway!.StartCount);  // no gateway call at all
        Assert.Equal("Running", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.Contains("Skipped 1 map(s)", vm.Status);
        Assert.Contains("Resume", vm.Status);
        Assert.Contains("Customers V3", vm.Status);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn && e.Text.Contains("Customers V3"));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_map_whose_actions_the_gateway_did_not_report_is_still_sent()
    {
        // Regression for older gateways (and any response that omits detail.actions): unknown eligibility
        // must not be read as "supports nothing", or the screen refuses every action on every map.
        var connector = new FakeDualWriteConnector(new[] { MapWith("Legacy map", "Stopped") });
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single().IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StartAction);

        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Equal("Legacy map", Assert.Single(connector.LastGateway!.LastActionMaps).Name);
        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Skipped", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Running", vm.Maps.Single().State);
    }

    [Fact]
    public async Task The_confirm_dialog_lists_only_the_maps_that_will_be_sent()
    {
        // The dialog is the last chance to see what is about to happen: listing a map that has already been
        // excluded would make it the wrong last chance.
        var connector = new FakeDualWriteConnector(MixedEligibilityMaps());
        var dialogs = new FakeDialogs(confirm: false);
        var vm = new DualWriteOpsViewModel(connector, Env, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        foreach (var row in vm.Maps)
        {
            row.IsSelected = true;
        }

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        var request = dialogs.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(2, request!.Targets.Count);
        Assert.Contains("2 map(s)", request.Title);   // the count matches the list, not the selection
        Assert.DoesNotContain(request.Targets, t => t.Contains("Released products"));
        Assert.Equal(0, connector.LastGateway!.StartCount);   // declined, so still nothing submitted
    }

    // --- #166: a submitted action is never reported as a failure just because it can't be polled ---

    [Fact]
    public async Task An_action_with_no_request_id_reports_it_as_submitted_and_still_refreshes()
    {
        // 202 + empty body (or a bare unlabelled id): the action WAS submitted, there is just nothing to
        // poll. Calling GetStatusAsync with the blank id threw, which surfaced as "failed" and skipped the
        // refresh — leaving a stale grid that invites a duplicate submit.
        var connector = new FakeDualWriteConnector(emptyRequestId: true);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Contains("submitted", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, connector.LastGateway!.GetMapsCount);   // load + the post-action refresh
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("submitted", StringComparison.OrdinalIgnoreCase));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task An_action_that_outruns_the_poll_timeout_keeps_the_request_id_and_refreshes()
    {
        // The action is in flight; only our polling gave up. Saying "timed out" while discarding the
        // request id and skipping the refresh is how a second Initial sync gets submitted.
        var connector = new FakeDualWriteConnector(pollsBeforeTerminal: int.MaxValue);
        var vm = new DualWriteOpsViewModel(connector, Env, new FakeDialogs(confirm: true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromMilliseconds(200));
        await vm.LoadCommand.ExecuteAsync(null);
        // Initial sync targets a Stopped map — a Running seed reports only Stop/Pause as available (#168).
        vm.Maps.Single(m => m.Name == "Chart of accounts").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.InitialAction);

        Assert.Contains("submitted", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("still running", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("req-001", vm.Status);                  // the id survives, so it can be chased up
        Assert.Equal(2, connector.LastGateway!.GetMapsCount);   // load + the post-action refresh
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task An_action_whose_status_check_throws_is_reported_as_submitted_and_still_refreshes()
    {
        // The submit succeeded; only GetStatusAsync broke (gateway 500 / network blip / non-JSON body).
        // That reached RunAction's outer catch as "Stop failed: …" and skipped the refresh, so the user was
        // told a submitted action had failed while looking at the pre-action states.
        var connector = new FakeDualWriteConnector(failStatusCheck: true);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Contains("submitted", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status check failed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stop failed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("500", vm.Status);                        // the reason survives, in one line
        Assert.DoesNotContain("<html", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, connector.LastGateway!.GetMapsCount);      // load + the post-action refresh
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Customers V3").State);
        // Warn, not Err: the action most likely succeeded — only the report on it didn't.
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("status check failed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.GatewayLog, e => e.Kind == LogKind.Err);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_user_cancel_during_polling_is_still_reported_as_a_cancel()
    {
        // User cancellation stops polling; it must not erase the submission or imply rollback.
        var connector = new FakeDualWriteConnector(pollsBeforeTerminal: int.MaxValue);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        var running = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        vm.RunActionCancelCommand.Execute(null);
        await running;

        Assert.Equal(1, connector.LastGateway!.StartCount);   // the submit happened before the cancel
        Assert.Contains("Cancel", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status check failed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    // --- #152: the session is pinned to the environment it was connected for ---

    [Fact]
    public async Task An_action_is_refused_after_the_active_environment_changes()
    {
        var connector = new FakeDualWriteConnector();
        var dialogs = new FakeDialogs(confirm: true);
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(connector, env.Get, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        env.Current = OtherEnv();   // shell switched; the cached session still belongs to env1

        Assert.False(vm.RunActionCommand.CanExecute(vm.StopAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);   // hard guard, not just CanExecute

        Assert.Equal(0, connector.LastGateway!.StartCount);      // no gateway call at all
        Assert.Equal(0, dialogs.Calls);                          // refused before the confirm dialog
        Assert.Equal("Running", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.Contains("reconnect", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("Contoso") && e.Text.Contains("Fabrikam"));
    }

    [Fact]
    public async Task An_action_is_refused_after_the_same_profile_id_is_repointed()
    {
        var connector = new FakeDualWriteConnector();
        var dialogs = new FakeDialogs(confirm: true);
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(connector, env.Get, dialogs,
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        env.Current = env.Current! with { Url = "https://replacement.operations.dynamics.com" };

        Assert.False(vm.RunActionCommand.CanExecute(vm.StopAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        Assert.Equal(0, connector.LastGateway!.StartCount);
        Assert.Equal(0, dialogs.Calls);
        Assert.Contains("reconnect", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_action_still_runs_while_the_active_environment_is_unchanged()
    {
        var connector = new FakeDualWriteConnector();
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(connector, env.Get, new FakeDialogs(confirm: true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        Assert.True(vm.RunActionCommand.CanExecute(vm.StopAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reconnecting_after_the_switch_restores_the_actions()
    {
        var connector = new FakeDualWriteConnector();
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(connector, env.Get, new FakeDialogs(confirm: true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5));
        await vm.LoadCommand.ExecuteAsync(null);
        env.Current = OtherEnv();

        await vm.LoadCommand.ExecuteAsync(null);   // explicit reconnect (never automatic)
        vm.Maps.First().IsSelected = true;

        Assert.True(vm.RunActionCommand.CanExecute(vm.StopAction));
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        Assert.Equal(1, connector.LastGateway!.StartCount);
    }

    // --- #53: debug-mode toggle ---

    private sealed class ScriptedODataClient : IODataClient
    {
        private readonly string _getBody;
        private readonly Action<int>? _afterCall;
        public List<(string Method, string Path, string? Body)> Calls { get; } = new();

        /// <param name="afterCall">Invoked with the 1-based call ordinal once the response is prepared —
        /// lets a test switch the active environment "during" a multi-request debug toggle.</param>
        public ScriptedODataClient(string getBody, Action<int>? afterCall = null)
        {
            _getBody = getBody;
            _afterCall = afterCall;
        }

        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
            => SendAsync(method, path, body, null, ct);

        public Task<ODataResponse> SendAsync(string method, string path, string? body,
            IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
        {
            Calls.Add((method, path, body));
            var resp = method == "GET"
                ? new ODataResponse(200, "OK", _getBody, 1)
                : new ODataResponse(204, "No Content", string.Empty, 1);
            _afterCall?.Invoke(Calls.Count);
            return Task.FromResult(resp);
        }
    }

    private sealed class FixedMetadata : IMetadataService
    {
        public void Invalidate() { }

        private readonly IReadOnlyList<EntitySet> _entities;
        public FixedMetadata(params string[] names) =>
            _entities = names.Select(n => new EntitySet(n, "DualWrite", 5, "Id", false, string.Empty)).ToList();
        public IReadOnlyList<EntitySet> GetEntities() => _entities;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static DualWriteOpsViewModel MakeDebugVm(IODataClient odata, IMetadataService metadata) =>
        new(new FakeDualWriteConnector(), Env, new FakeDialogs(true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: metadata);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Debug_confirmation_decline_has_zero_metadata_and_HTTP_side_effects(bool enabled)
    {
        var dialogs = new FakeDialogs(false);
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), Env, dialogs, odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        await (enabled ? vm.EnableDebugForSelectedCommand : vm.DisableDebugForSelectedCommand).ExecuteAsync(null);

        Assert.Equal(1, dialogs.Calls);
        Assert.Equal(0, metadata.Calls);
        Assert.Empty(odata.Calls);
        Assert.Contains("No debug changes sent", vm.DebugStatus);
        Assert.False(vm.MutationInProgress);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData("noSession")]
    [InlineData("identityChanged")]
    [InlineData("noSelection")]
    [InlineData("noUrl")]
    [InlineData("noProject")]
    public async Task Invalid_debug_target_is_rejected_before_confirmation_or_metadata(string invalid)
    {
        var env = new EnvSwitch();
        if (invalid == "noUrl") env.Current = env.Current! with { Url = string.Empty };
        var maps = new[] { MapWith("One", "Running") with { ProjectId = invalid == "noProject" ? " " : "project-a" } };
        var dialogs = new FakeDialogs(true);
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(maps), env.Get, dialogs, odata: odata, metadata: metadata);
        if (invalid != "noSession")
        {
            await vm.LoadCommand.ExecuteAsync(null);
            vm.Maps.Single().IsSelected = invalid != "noSelection";
        }
        if (invalid == "identityChanged") env.Current = env.Current! with { Tenant = "other-tenant" };

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await vm.DisableDebugForSelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, dialogs.Calls);
        Assert.Equal(0, metadata.Calls);
        Assert.Empty(odata.Calls);
        Assert.False(vm.MutationInProgress);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Debug_confirmation_acceptance_uses_the_exact_captured_projects_and_scope(bool enabled)
    {
        var maps = new[]
        {
            MapWith("One", "Running") with { ProjectId = "project-a" },
            MapWith("Also one", "Running") with { ProjectId = "project-a" },
            MapWith("Two", "Running") with { ProjectId = "project-b" },
            MapWith("Missing", "Running") with { ProjectId = " " },
            MapWith("Unselected", "Running") with { ProjectId = "project-c" },
        };
        var dialogs = new ControlledDebugDialogs();
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(maps), Env, dialogs, odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        foreach (var row in vm.Maps) row.IsSelected = row.Name != "Unselected";

        var operation = (enabled ? vm.EnableDebugForSelectedCommand : vm.DisableDebugForSelectedCommand).ExecuteAsync(null);
        await WaitForDebugConfirmation(operation, dialogs);
        var request = Assert.Single(dialogs.Requests);
        Assert.Equal($"{(enabled ? "Enable" : "Disable")} debug mode for 2 project(s)?", request.Title);
        Assert.Contains("Contoso", request.Message);
        Assert.Contains("https://contoso.operations.dynamics.com", request.Message);
        Assert.Contains("project-level", request.Message);
        Assert.Contains("logging", request.Message);
        Assert.Equal(new[] { "One · project-a", "Also one · project-a", "Two · project-b" }, request.Targets);
        Assert.Contains("1 selected map(s) without a project id", request.Caveat);
        Assert.Equal(0, metadata.Calls);
        Assert.Empty(odata.Calls);
        foreach (var row in vm.Maps) row.IsSelected = row.Name == "Unselected";
        dialogs.Answer.TrySetResult(true);
        await operation;

        Assert.Equal(new[]
        {
            "data/DualWriteProjectConfigurations?$filter=ProjectId eq 'project-a'",
            "data/DualWriteProjectConfigurations?$filter=ProjectId eq 'project-b'",
        }, odata.Calls.Where(c => c.Method == "GET").Select(c => c.Path));
        var patches = odata.Calls.Where(c => c.Method == "PATCH").ToList();
        Assert.Equal(2, patches.Count);
        Assert.All(patches, p => Assert.Equal(enabled ? "{\"IsDebugMode\":\"Yes\"}" : "{\"IsDebugMode\":\"No\"}", p.Body));
        Assert.False(vm.MutationInProgress);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Debug_confirmation_identity_change_sends_nothing(bool enabled)
    {
        var env = new EnvSwitch();
        var dialogs = new ControlledDebugDialogs();
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, dialogs, odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;
        var operation = (enabled ? vm.EnableDebugForSelectedCommand : vm.DisableDebugForSelectedCommand).ExecuteAsync(null);
        await WaitForDebugConfirmation(operation, dialogs);
        env.Current = env.Current! with { Tenant = "different-tenant" };
        dialogs.Answer.TrySetResult(true);
        await operation;

        Assert.Equal(0, metadata.Calls);
        Assert.Empty(odata.Calls);
        Assert.Contains("not sent", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.MutationInProgress);
    }

    [Theory]
    [InlineData(true, "accept")]
    [InlineData(false, "decline")]
    [InlineData(true, "fault")]
    [InlineData(false, "cancel")]
    public async Task Debug_confirmation_disposal_never_dispatches_or_publishes_late_status(bool enabled, string outcome)
    {
        var dialogs = new ControlledDebugDialogs();
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), Env, dialogs, odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;
        var operation = (enabled ? vm.EnableDebugForSelectedCommand : vm.DisableDebugForSelectedCommand).ExecuteAsync(null);
        await WaitForDebugConfirmation(operation, dialogs);
        vm.Dispose();
        vm.DebugStatus = "state after disposal";
        if (outcome == "fault") dialogs.Answer.TrySetException(new InvalidOperationException("dialog failed"));
        else if (outcome == "cancel") dialogs.Answer.TrySetException(new OperationCanceledException());
        else dialogs.Answer.TrySetResult(outcome == "accept");
        await operation;

        Assert.Equal("state after disposal", vm.DebugStatus);
        Assert.Equal(0, metadata.Calls);
        Assert.Empty(odata.Calls);
        Assert.False(vm.MutationInProgress);
    }

    [Theory]
    [InlineData(true, "fault")]
    [InlineData(false, "dialogCancel")]
    [InlineData(true, "commandCancel")]
    public async Task Debug_confirmation_fault_or_cancel_releases_the_lease_for_a_later_operation(bool enabled, string outcome)
    {
        var dialogs = new ControlledDebugDialogs();
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), Env, dialogs, odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;
        var command = enabled ? vm.EnableDebugForSelectedCommand : vm.DisableDebugForSelectedCommand;
        var operation = command.ExecuteAsync(null);
        await WaitForDebugConfirmation(operation, dialogs);
        if (outcome == "fault") dialogs.Answer.TrySetException(new InvalidOperationException("dialog failed"));
        else if (outcome == "dialogCancel") dialogs.Answer.TrySetException(new OperationCanceledException());
        else { command.Cancel(); dialogs.Answer.TrySetResult(true); }
        await operation;

        Assert.Empty(odata.Calls);
        Assert.Equal(0, metadata.Calls);
        Assert.Contains("sent", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reconnect", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.MutationInProgress);
        Assert.True(vm.EnableDebugForSelectedCommand.CanExecute(null));
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        Assert.Single(odata.Calls, c => c.Method == "PATCH");
        Assert.Equal(2, dialogs.Requests.Count);
    }

    [Fact]
    public async Task Debug_command_cancel_completes_without_answering_confirmation_and_late_approval_cannot_dispatch()
    {
        var dialogs = new ControlledDebugDialogs();
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var connector = new CountingConnector();
        var vm = new DualWriteOpsViewModel(connector, Env, dialogs, odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        var operation = vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await WaitForDebugConfirmation(operation, dialogs);
        vm.EnableDebugForSelectedCommand.Cancel();
        var winner = await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        var completedWithoutAnswer = ReferenceEquals(winner, operation);

        // Always release the legacy fake for RED cleanup. After the fix this is a late answer to an
        // abandoned confirmation and must have no effect.
        dialogs.Answer.TrySetResult(true);
        await operation;

        Assert.True(completedWithoutAnswer);
        Assert.Equal(0, metadata.Calls);
        Assert.Empty(odata.Calls);
        Assert.False(vm.MutationInProgress);
        Assert.False(vm.IsBusy);
        Assert.True(vm.EnableDebugForSelectedCommand.CanExecute(null));
        Assert.Equal(1, connector.Calls);
    }

    [Fact]
    public async Task Debug_confirmation_owner_refuses_direct_competitors_without_extra_dialogs()
    {
        var dialogs = new ControlledDebugDialogs();
        var connector = new CountingConnector();
        var metadata = new CountingMetadata("DualWriteProjectConfigurations");
        var odata = new ScriptedODataClient(DebugRecord);
        var vm = new DualWriteOpsViewModel(connector, Env, dialogs, odata: odata, metadata: metadata);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;
        var operation = vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await WaitForDebugConfirmation(operation, dialogs);
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        await vm.DisableDebugForSelectedCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Equal(1, connector.Calls);
        Assert.Equal(0, connector.LastGateway!.StartCount);
        Assert.Equal(0, metadata.Calls);
        Assert.Empty(odata.Calls);
        Assert.True(vm.MutationInProgress);
        Assert.True(vm.IsBusy);
        dialogs.Answer.TrySetResult(false);
        await operation;
        Assert.False(vm.MutationInProgress);
        Assert.True(vm.LoadCommand.CanExecute(null));
    }

    [Fact]
    public void Debug_toggle_is_disabled_until_connected_with_a_selection()
    {
        var vm = MakeDebugVm(new ScriptedODataClient("{}"), new FixedMetadata("DualWriteProjectConfigurations"));

        Assert.False(vm.EnableDebugForSelectedCommand.CanExecute(null));
        Assert.False(vm.DisableDebugForSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Enabling_debug_patches_IsDebugMode_on_the_selected_maps_project_config()
    {
        const string record = "{\"value\":[{\"@odata.id\":\"https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)\",\"IsDebugMode\":\"No\"}]}";
        var odata = new ScriptedODataClient(record);
        var vm = MakeDebugVm(odata, new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        var patch = odata.Calls.Single(c => c.Method == "PATCH");
        Assert.Equal("https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)", patch.Path);
        Assert.Equal(DualWriteDebugMode.BuildPatchBody(true), patch.Body);
        Assert.Contains("enabled", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Debug_toggle_explains_when_the_config_entity_is_absent_from_metadata()
    {
        var vm = MakeDebugVm(new ScriptedODataClient("{}"), new FixedMetadata("SomeOtherEntity"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        Assert.Contains("DualWriteProjectConfiguration", vm.DebugStatus);
    }

    [Fact]
    public async Task Debug_toggle_is_refused_after_the_active_environment_changes()
    {
        // The project ids come from the old session's maps but _odata resolves the active environment per
        // call — the one operation would otherwise straddle two environments.
        const string record = "{\"value\":[{\"@odata.id\":\"https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)\",\"IsDebugMode\":\"No\"}]}";
        var odata = new ScriptedODataClient(record);
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, new FakeDialogs(false),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        env.Current = OtherEnv();

        Assert.False(vm.EnableDebugForSelectedCommand.CanExecute(null));
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);   // hard guard, not just CanExecute

        Assert.Empty(odata.Calls);                                   // no GET, no PATCH
        Assert.Contains("reconnect", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconnect", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("Contoso") && e.Text.Contains("Fabrikam"));
    }

    // The entry guard above expires at the first await — these cover a switch landing mid-toggle.

    private const string DebugRecord =
        "{\"value\":[{\"@odata.id\":\"https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)\",\"IsDebugMode\":\"No\"}]}";

    [Fact]
    public async Task A_switch_between_the_config_read_and_the_patch_stops_before_any_write()
    {
        var env = new EnvSwitch();
        // Switch as the config GET returns: the PATCH would resolve env2 while carrying env1's project id.
        var odata = new ScriptedODataClient(DebugRecord, afterCall: n =>
        {
            if (n == 1)
            {
                env.Current = OtherEnv();
            }
        });
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, new FakeDialogs(true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        Assert.Single(odata.Calls);                                  // the GET only…
        Assert.DoesNotContain(odata.Calls, c => c.Method == "PATCH"); // …no write at all
        Assert.Contains("reconnect", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconnect", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("Contoso") && e.Text.Contains("Fabrikam"));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_switch_after_the_first_patch_stops_and_reports_how_many_applied()
    {
        var env = new EnvSwitch();
        // Two selected maps = two project ids = GET, PATCH, GET, PATCH. Switch as the first PATCH returns.
        var odata = new ScriptedODataClient(DebugRecord, afterCall: n =>
        {
            if (n == 2)
            {
                env.Current = OtherEnv();
            }
        });
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, new FakeDialogs(true),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;
        vm.Maps.Single(m => m.Name == "Vendors V2").IsSelected = true;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, odata.Calls.Count(c => c.Method == "PATCH")); // the second target is never touched
        Assert.Equal(2, odata.Calls.Count);                           // GET + PATCH, then stopped
        // The applied PATCH stands (it was valid for the environment it was issued against) — the status has
        // to say so rather than implying the whole toggle happened, or that none of it did.
        Assert.Contains("1 of 2", vm.DebugStatus);
        Assert.Contains("reconnect", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconnect", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Warn
            && e.Text.Contains("Contoso") && e.Text.Contains("Fabrikam"));
        Assert.False(vm.IsBusy);
    }

    // --- gateway log (self-diagnosis: which host, which cid, what happened) ---

    [Fact]
    public async Task Successful_load_logs_the_gateway_host_the_cid_and_the_map_count()
    {
        var vm = MakeVm(new FakeDualWriteConnector());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasGatewayLog);
        Assert.Contains(vm.GatewayLog, e => e.Text.Contains("fake-gateway.dual-write.example"));
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Ok && e.Text.Contains("fake-cid"));
        Assert.Contains(vm.GatewayLog, e => e.Text.Contains("Loaded") && e.Text.Contains("map"));
    }

    [Fact]
    public async Task A_connect_failure_is_logged_as_an_error_line()
    {
        var vm = MakeVm(FakeDualWriteConnector.ThatFails("no connection (cid) for this environment"));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Err && e.Text.Contains("no connection"));
    }

    // #168: the in-app log dies with the window, but a dual-write connection failure is exactly what a user
    // reports after the fact — so the Warn/Err lines must also reach Trace, where the session log keeps them.
    [Fact]
    public async Task An_error_line_also_reaches_Trace_so_the_session_log_keeps_it()
    {
        using var trace = new TraceCapture();
        var vm = MakeVm(FakeDualWriteConnector.ThatFails("no connection (cid) for this environment"));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains("no connection (cid) for this environment", trace.Text);
    }

    // The session log's secrecy bar: a DualWriteGatewayException message embeds up to 500 characters of the
    // gateway's raw response body. On screen that is the user reading about their own gateway; on disk it is
    // a response body in a file people attach to bug reports.
    [Fact]
    public async Task A_gateway_failure_shows_the_response_body_on_screen_but_never_traces_it()
    {
        using var trace = new TraceCapture();
        var failure = new DualWriteGatewayException(
            "Dual-write gateway request failed: 502 BadGateway. {\"error\":\"GATEWAY-RESPONSE-BODY-MARKER\"}",
            HttpStatusCode.BadGateway);
        var vm = MakeVm(FakeDualWriteConnector.ThatFailsWith(failure));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Err && e.Text.Contains("GATEWAY-RESPONSE-BODY-MARKER"));
        Assert.DoesNotContain("GATEWAY-RESPONSE-BODY-MARKER", trace.Text);
        // The status still reaches the file, so the redacted line is still worth having.
        Assert.Contains("the gateway returned 502", trace.Text);
    }

    /// <summary>
    /// The other body-quoting Core exception: the #166 non-JSON guard, which puts the first line of an HTML
    /// sign-in or proxy page into its message. Type-matched redaction has to cover this one too — matching
    /// only <see cref="DualWriteGatewayException"/> let it through to the persisted log unredacted.
    /// </summary>
    [Fact]
    public async Task A_non_json_gateway_response_is_explained_on_screen_but_its_body_is_never_traced()
    {
        using var trace = new TraceCapture();
        var vm = MakeVm(FakeDualWriteConnector.ThatFailsWith(NonJsonFailure(
            "<!DOCTYPE html><title>Sign in</title><body>NON-JSON-BODY-MARKER</body>")));

        await vm.LoadCommand.ExecuteAsync(null);

        // On screen the user still gets the fragment that identifies the interstitial...
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Err && e.Text.Contains("<!DOCTYPE html>"));
        // ...but neither the body fragment nor the "first line:" excerpt that carries it reaches the file.
        Assert.DoesNotContain("NON-JSON-BODY-MARKER", trace.Text);
        Assert.DoesNotContain("<!DOCTYPE html>", trace.Text);
        Assert.DoesNotContain("first line:", trace.Text);
        // The diagnosis survives: a log reader still learns the gateway answered with something non-JSON.
        Assert.Contains("non-JSON response", trace.Text);
    }

    // Mints the genuine Core exception by running the real parser over a non-JSON body, so the test asserts
    // against Core's actual message rather than a hand-copied imitation that could drift out of step with it.
    private static DualWriteGatewayResponseException NonJsonFailure(string responseBody) =>
        Assert.Throws<DualWriteGatewayResponseException>(() => DualWriteResponseParser.ParseMaps(responseBody));

    [Fact]
    public async Task The_successful_chatter_stays_on_screen_and_is_not_traced()
    {
        using var trace = new TraceCapture();
        var vm = MakeVm(new FakeDualWriteConnector());

        await vm.LoadCommand.ExecuteAsync(null);

        // Info/Ok is live-diagnosis noise; only failures earn a line in the file.
        Assert.Contains(vm.GatewayLog, e => e.Kind == LogKind.Ok && e.Text.Contains("fake-cid"));
        Assert.DoesNotContain("fake-cid", trace.Text);
        Assert.DoesNotContain("Connecting to", trace.Text);
    }

    // --- #167: a terminal request whose map states haven't propagated yet ---

    [Fact]
    public async Task A_completed_action_whose_state_has_not_propagated_shows_the_pre_action_state()
    {
        // The fake used to mutate the map states inside StartActionAsync, so the post-action refresh could
        // never come back with pre-action states — the real gateway's lag (request terminal, map list not
        // yet updated) was unobservable, and any "the grid caught up" assertion passed for free.
        // deferStateUntilPolls: 2 withholds the change past the single terminal poll.
        var connector = new FakeDualWriteConnector(deferStateUntilPolls: 2);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        // The action completed as far as the gateway is concerned…
        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Contains("completed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, connector.LastGateway!.GetMapsCount);   // load + the post-action refresh
        // …but the refresh legitimately reported the old state, and the screen shows exactly that rather
        // than a state it invented from the action it just sent.
        Assert.True(connector.LastGateway!.HasDeferredState);
        Assert.Equal("Running", vm.Maps.Single(m => m.Name == "Customers V3").State);
        Assert.False(vm.IsBusy);

        // Once the gateway catches up, its own map list carries the new state — so the next read the screen
        // does reports it, with no re-submit.
        connector.LastGateway!.ReleaseDeferredState();
        var caughtUp = await connector.LastGateway!.GetMapsAsync("fake-cid", TestContext.Current.CancellationToken);

        Assert.Equal("Stopped", caughtUp.Single(m => m.Name == "Customers V3").State);
        Assert.Equal(1, connector.LastGateway!.StartCount);     // still exactly one submit
    }

    [Fact]
    public async Task Deferring_is_opt_in_so_the_default_still_reflects_the_action_immediately()
    {
        var connector = new FakeDualWriteConnector();
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.False(connector.LastGateway!.HasDeferredState);
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Customers V3").State);
    }

    [Fact]
    public async Task A_deferred_state_can_be_released_by_polling_alone()
    {
        // Three non-terminal polls precede the terminal one, so a release budget of 2 lands mid-poll and
        // the refresh sees the new state — the "it did catch up in time" half of the same seam.
        var connector = new FakeDualWriteConnector(pollsBeforeTerminal: 3, deferStateUntilPolls: 2);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.False(connector.LastGateway!.HasDeferredState);
        Assert.Equal("Stopped", vm.Maps.Single(m => m.Name == "Customers V3").State);
    }

    [Fact]
    public async Task Sequential_deferred_actions_each_get_their_own_poll_budget()
    {
        // #167 P2 (PR #185 review): the fake's status-poll counter used to be shared across every action
        // run on the same gateway, so polls already spent releasing action 1's deferral counted towards
        // action 2's release threshold too — action 2 could be released before its OWN poll count elapsed.
        var connector = new FakeDualWriteConnector(deferStateUntilPolls: 2);
        var vm = MakeVm(connector, confirm: true);
        await vm.LoadCommand.ExecuteAsync(null);

        // Action 1: Stop "Customers V3". A single automatic poll (pollsBeforeTerminal defaults to 0) does
        // not reach the release threshold of 2, so it stays deferred — same seam as the test above.
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = true;
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        Assert.True(connector.LastGateway!.HasDeferredState);

        // The gateway "catches up" on its own — action 1 is fully done with, no more polling against it.
        connector.LastGateway!.ReleaseDeferredState();
        Assert.False(connector.LastGateway!.HasDeferredState);

        // Action 2: Pause "Vendors V2" on the SAME gateway, same 2-poll threshold. If the poll counter
        // carried over from action 1 (which already consumed 1 poll), action 2's own single automatic poll
        // would push the shared count to 2 and release action 2's deferral a full poll early.
        vm.Maps.Single(m => m.Name == "Customers V3").IsSelected = false;
        vm.Maps.Single(m => m.Name == "Vendors V2").IsSelected = true;
        await vm.RunActionCommand.ExecuteAsync(vm.PauseAction);

        // With a fresh, per-action budget, action 2's one poll is not enough to release it either — its map
        // state must stay exactly what it was before action 2 (Running), not the withheld Paused state.
        Assert.True(connector.LastGateway!.HasDeferredState);
        Assert.Equal("Running", vm.Maps.Single(m => m.Name == "Vendors V2").State);

        // The withheld change is real (not lost) and specific to action 2 — releasing it now shows Pause
        // applied, while action 1's own result (Stopped) is undisturbed.
        connector.LastGateway!.ReleaseDeferredState();
        var caughtUp = await connector.LastGateway!.GetMapsAsync("fake-cid", TestContext.Current.CancellationToken);
        Assert.Equal("Paused", caughtUp.Single(m => m.Name == "Vendors V2").State);
        Assert.Equal("Stopped", caughtUp.Single(m => m.Name == "Customers V3").State);
    }

    [Fact]
    public async Task Reconnecting_resets_the_log_to_only_the_current_attempt()
    {
        // A failed first attempt should not interleave with a later one — the log isolates the latest connect.
        var vm = MakeVm(FakeDualWriteConnector.ThatFails("first attempt failed"));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains(vm.GatewayLog, e => e.Text.Contains("first attempt failed"));

        // Re-point at a working connector and reconnect (the VM connects fresh each Load).
        var vm2 = MakeVm(new FakeDualWriteConnector());
        await vm2.LoadCommand.ExecuteAsync(null);
        await vm2.LoadCommand.ExecuteAsync(null); // second connect on the same VM

        // Exactly one "Connecting…" line — the prior attempt's entries were cleared, not appended to.
        Assert.Equal(1, vm2.GatewayLog.Count(e => e.Text.StartsWith("Connecting")));
    }

    private sealed class CountingMetadata : IMetadataService
    {
        private readonly IReadOnlyList<EntitySet> _entities;
        public int Calls { get; private set; }
        public CountingMetadata(params string[] names) =>
            _entities = names.Select(n => new EntitySet(n, "DualWrite", 5, "Id", false, string.Empty)).ToList();
        public void Invalidate() { }
        public IReadOnlyList<EntitySet> GetEntities()
        {
            Calls++;
            return _entities;
        }
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class GatedODataClient : IODataClient
    {
        private readonly string _body;
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public int Calls { get; private set; }
        public GatedODataClient(string body) => _body = body;
        public void Release() => _release.TrySetResult();
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default) =>
            SendAsync(method, path, body, null, ct);
        public async Task<ODataResponse> SendAsync(string method, string path, string? body,
            IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
        {
            Calls++;
            _entered.TrySetResult();
            await _release.Task.WaitAsync(ct);
            return method == "GET"
                ? new ODataResponse(200, "OK", _body, 1)
                : new ODataResponse(204, "No Content", string.Empty, 1);
        }
    }

    [Fact]
    public async Task Debug_toggle_is_refused_after_same_id_auth_edit()
    {
        const string record = "{\"value\":[{\"@odata.id\":\"https://contoso.operations.dynamics.com/data/DualWriteProjectConfigurations(1)\",\"IsDebugMode\":\"No\"}]}";
        var odata = new ScriptedODataClient(record);
        var env = new EnvSwitch();
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, new FakeDialogs(false),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: new FixedMetadata("DualWriteProjectConfigurations"));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps.First().IsSelected = true;

        env.Current = env.Current! with { ClientId = "replacement-client", AuthMode = FoAuthMode.ClientSecret };
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        Assert.Empty(odata.Calls);
        Assert.Contains("reconnect", vm.DebugStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_session_returned_after_dispose_is_disposed_and_never_assigned()
    {
        var connector = new GatedDisposableConnector();
        var vm = MakeVm(connector);

        var load = vm.LoadCommand.ExecuteAsync(null);
        await connector.Entered;
        vm.Dispose();
        connector.Release(Env());
        await load;

        Assert.True(connector.Gateway.Disposed);
        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Maps);
    }

    [Fact]
    public async Task Disposed_operations_refuses_a_new_connect()
    {
        var connector = new FakeDualWriteConnector();
        var vm = MakeVm(connector);
        vm.Dispose();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Null(connector.LastGateway);
        Assert.False(vm.IsConnected);
    }

    private sealed class GatedDisposableConnector : IDualWriteConnector
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DualWriteSession> _session =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public DisposableGateway Gateway { get; } = new();
        public CancellationToken ConnectToken { get; private set; }

        public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
        {
            ConnectToken = ct;
            _entered.TrySetResult();
            return _session.Task;
        }

        public void Release(EnvProfile env) => _session.TrySetResult(
            new DualWriteSession(Gateway, "cid", "Connection", env, "https://gateway.example"));
    }

    private sealed class DisposableGateway : IDualWriteGateway, IDisposable
    {
        private readonly FakeCoreDualWriteGateway _inner = new(FakeDualWriteConnector.SeedMaps());
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default) =>
            _inner.GetEnvironmentAsync(foIdentifier, cancellationToken);
        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default) =>
            _inner.GetMapsAsync(cid, cancellationToken);
        public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default) =>
            _inner.StartActionAsync(action, maps, cid, cancellationToken);
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
        public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities, bool forceReset, CancellationToken cancellationToken = default) =>
            _inner.ResetLinksAsync(cid, connectionSet, legalEntities, forceReset, cancellationToken);
        public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName, IReadOnlyList<string> keyFields, CancellationToken cancellationToken = default) =>
            _inner.ApplyIntegrationKeysAsync(datasetName, ceEntityName, keyFields, cancellationToken);
    }
}
