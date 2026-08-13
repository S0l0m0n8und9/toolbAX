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
        // Guards the catch above from swallowing a genuine cancel: the token the user cancelled is ours, so
        // this must stay "Cancelled." rather than being dressed up as a submitted-but-unpollable action.
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
        private readonly IReadOnlyList<EntitySet> _entities;
        public FixedMetadata(params string[] names) =>
            _entities = names.Select(n => new EntitySet(n, "DualWrite", 5, "Id", false, string.Empty)).ToList();
        public IReadOnlyList<EntitySet> GetEntities() => _entities;
        public IReadOnlyList<EntityField>? GetFields(string entityName) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static DualWriteOpsViewModel MakeDebugVm(IODataClient odata, IMetadataService metadata) =>
        new(new FakeDualWriteConnector(), Env, new FakeDialogs(false),
            pollInterval: TimeSpan.FromMilliseconds(1), actionTimeout: TimeSpan.FromSeconds(5),
            odata: odata, metadata: metadata);

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
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, new FakeDialogs(false),
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
        var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(), env.Get, new FakeDialogs(false),
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
}
