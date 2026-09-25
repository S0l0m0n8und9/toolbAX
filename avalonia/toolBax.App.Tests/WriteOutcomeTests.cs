using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Threading;
using FoToolbox.Core.DualWrite;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ToolBax.App.Views;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class WriteOutcomeTests
{
    private static EnvProfile Env() => new("e", "Captured", "https://fo.example", "tenant", "USMF", "Tier 1", EnvStatus.Connected);
    private sealed class Dialogs : IDialogService
    {
        public TaskCompletionSource<bool>? Gate { get; init; }
        public List<ConfirmRequest> Requests { get; } = new();
        public Task<bool> ConfirmAsync(ConfirmRequest request) { Requests.Add(request); return Gate?.Task ?? Task.FromResult(true); }
    }

    private sealed class Client(Func<string, string, CancellationToken, Task<ODataResponse>> send) : IODataClient
    {
        public List<(string Method, string Path)> Calls { get; } = new();
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        { Calls.Add((method, path)); return send(method, path, ct); }
    }

    [Fact]
    public async Task Post_lost_reply_after_server_change_is_unknown_not_failed_or_unsent()
    {
        var changed = false;
        var client = new Client((_, _, _) => { changed = true; throw new InvalidOperationException("lost reply"); });
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(changed);
        Assert.Contains("unknown", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not sent", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_confirmation_cancel_finishes_without_answer_and_cannot_send_later()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, _, _) => Task.FromResult(new ODataResponse(204, "OK", "", 0)));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs { Gate = gate }, activeEnv: Env);
        var send = vm.SendCommand.ExecuteAsync(null);
        try
        {
            vm.SendCancelCommand.Execute(null);
            await send.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Empty(client.Calls);
            Assert.Contains("not sent", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally { gate.TrySetResult(true); await send; }
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Post_incomplete_2xx_is_acknowledged_but_unconfirmed()
    {
        var client = new Client((_, _, _) => Task.FromResult(new ODataResponse(201, "Created", "", 0)
            { DispatchStarted = true, BodyComplete = false, BodyReadError = "IOException" }));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Contains("acknowledged", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unconfirmed", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.SendSucceeded);
    }

    [Fact]
    public async Task Ops_poll_cancel_keeps_submitted_evidence()
    {
        var connector = new FakeDualWriteConnector(pollsBeforeTerminal: int.MaxValue);
        using var vm = new DualWriteOpsViewModel(connector, Env, new Dialogs(), pollInterval: TimeSpan.FromSeconds(1));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        var run = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        vm.RunActionCancelCommand.Execute(null);
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Contains("submitted", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stopped waiting", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Readback_uses_captured_resource_and_preserves_unknown_after_successful_get(string method)
    {
        var client = new Client((verb, _, _) => verb == "GET" ? Task.FromResult(new ODataResponse(200, "OK", "current", 1)) : throw new InvalidOperationException("lost"));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env) { Method = method, Path = "/data/X(1)" };
        await vm.SendCommand.ExecuteAsync(null);
        var receipt = vm.LastReceipt;
        vm.Path = "/data/Other(2)";
        vm.Method = "POST";
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Equal(("GET", "https://fo.example/data/X(1)"), client.Calls.Last());
        Assert.Same(receipt, vm.LastReceipt);
        Assert.True(vm.LastReceipt!.Observation.Unconfirmed);
        Assert.Contains("does not prove", vm.ReadbackText);
        Assert.Equal(2, client.Calls.Count);
    }

    [Theory]
    [InlineData("https://foreign.example/data/X(1)")]
    [InlineData("https://user@fo.example/data/X(1)")]
    [InlineData("https://fo.example/data/X(1)#secret")]
    [InlineData(null)]
    public async Task Post_unsafe_or_absent_locator_never_gets_guessed_or_dispatched(string? locator)
    {
        var client = new Client((_, _, _) => Task.FromResult(new ODataResponse(201, "Created", "", 0,
            locator is null ? null : new Dictionary<string, string> { ["Location"] = locator }) { DispatchStarted = true }));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.False(vm.ReconcileCommand.CanExecute(null));
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Single(client.Calls);
        Assert.Contains("Query Builder", vm.ReconcileReason);
    }

    [Fact]
    public async Task Post_relative_server_locator_is_resolved_to_captured_origin()
    {
        var client = new Client((verb, _, _) => Task.FromResult(new ODataResponse(verb == "POST" ? 201 : 200, "OK", "", 0,
            new Dictionary<string, string> { ["OData-EntityId"] = "data/X(42)" }) { DispatchStarted = true }));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env);
        await vm.SendCommand.ExecuteAsync(null);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Equal(("GET", "https://fo.example/data/X(42)"), client.Calls.Last());
    }

    [Fact]
    public async Task Rejected_direct_send_and_readback_do_not_steal_owner_cancellation_or_receipt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client(async (_, _, ct) => { entered.TrySetResult(); await new TaskCompletionSource().Task.WaitAsync(ct); return new ODataResponse(204, "OK", "", 0); });
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env) { Method = "PATCH" };
        var send = vm.SendCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var receipt = vm.LastReceipt;
        Assert.True(vm.SendCommand.CanBeCanceled);
        await vm.SendCommand.ExecuteAsync(null);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Same(send, vm.SendCommand.ExecutionTask);
        Assert.Same(receipt, vm.LastReceipt);
        Assert.True(vm.MutationInProgress);
        vm.SendCommand.Cancel();
        await send.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Single(client.Calls);
        Assert.Contains("unknown", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Post_readback_drift_blocks_dispatch_and_discards_held_result()
    {
        var current = Env();
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((verb, _, _) => verb == "GET" ? gate.Task : Task.FromResult(new ODataResponse(204, "OK", "", 0)));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: () => current) { Method = "DELETE", Path = "/data/X(1)" };
        await vm.SendCommand.ExecuteAsync(null);
        current = current with { Tenant = "changed" };
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Single(client.Calls);
        Assert.Contains("disabled", vm.ReconcileReason);
        current = Env();
        var read = vm.ReconcileCommand.ExecuteAsync(null);
        current = current with { Url = "https://other.example" };
        gate.SetResult(new ODataResponse(200, "OK", "STALE", 0));
        await read;
        Assert.DoesNotContain("STALE", vm.ReadbackText);
    }

    [Fact]
    public async Task A_new_confirmation_names_an_unconfirmed_prior_attempt()
    {
        var dialogs = new Dialogs();
        var client = new Client((_, _, _) => throw new InvalidOperationException());
        using var vm = new PostBuilderViewModel(client, dialogs: dialogs, activeEnv: Env);
        await vm.SendCommand.ExecuteAsync(null);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Contains("prior", dialogs.Requests[1].Message);
        Assert.Contains("unconfirmed", dialogs.Requests[1].Message);
    }

    private sealed class Metadata : IMetadataService
    {
        public void Invalidate() { }
        public IReadOnlyList<EntitySet> GetEntities() => new[] { new EntitySet("DualWriteProjectConfigurations", "", 1, "Id", false, "") };
        public IReadOnlyList<EntityField>? GetFields(string name) => null;
        public Task LoadEntitiesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> LoadFieldsAsync(string name, CancellationToken ct = default) => Task.FromResult(false);
    }

    [Fact]
    public async Task Debug_partial_progress_and_readback_preserve_ack_unknown_and_unattempted_legs()
    {
        var maps = Enumerable.Range(1,3).Select(i => new DualWriteMap($"m{i}", $"Map {i}", $"Map {i}", $"p{i}", "Stopped", null, Array.Empty<DualWriteTemplate>())).ToArray();
        var patches = 0;
        var client = new Client((verb, _, _) => {
            if (verb == "GET") return Task.FromResult(new ODataResponse(200, "OK", "{\"value\":[{\"@odata.id\":\"https://fo.example/data/Config(1)\",\"IsDebugMode\":2}]}", 0));
            patches++;
            if (patches == 2) throw new InvalidOperationException("server changed but reply lost");
            return Task.FromResult(new ODataResponse(204, "OK", "", 0) { DispatchStarted = true });
        });
        using var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(maps), Env, new Dialogs(), odata: client, metadata: new Metadata());
        await vm.LoadCommand.ExecuteAsync(null);
        foreach (var map in vm.Maps) map.IsSelected = true;
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        var receipt = vm.LastDebugReceipt!;
        Assert.Equal(204, receipt.Projects[0].Observation!.StatusCode);
        Assert.True(receipt.Projects[1].Observation!.Unconfirmed);
        Assert.Null(receipt.Projects[2].Observation);
        Assert.Equal("Not attempted", receipt.Projects[2].Stage);
        Assert.Equal(2, patches);
        foreach (var map in vm.Maps) map.IsSelected = false;
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Equal(2, patches);
        Assert.Same(receipt, vm.LastDebugReceipt);
        Assert.Contains("p3", vm.ReadbackText);
        Assert.Contains("flag unknown", vm.ReadbackText);
        Assert.Contains("Original", vm.ReadbackText);
    }

    [Fact]
    public async Task Ops_poll_cancel_retains_id_and_reconcile_reads_it_without_resubmitting()
    {
        var connector = new FakeDualWriteConnector(pollsBeforeTerminal: int.MaxValue);
        using var vm = new DualWriteOpsViewModel(connector, Env, new Dialogs(), pollInterval: TimeSpan.FromSeconds(1));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        var running = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        var receipt = vm.LastLifecycleReceipt;
        await vm.RunActionCommand.ExecuteAsync(vm.StartAction);
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Same(running, vm.RunActionCommand.ExecutionTask);
        vm.RunActionCommand.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(vm.LastLifecycleReceipt!.RequestId);
        Assert.Same(receipt, vm.LastLifecycleReceipt);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Contains(vm.LastLifecycleReceipt.RequestId!, vm.ReadbackText);
        Assert.Contains("unchanged", vm.ReadbackText);
    }

    [Fact]
    public async Task Ops_no_request_id_reconciles_captured_maps_without_starting_again()
    {
        var connector = new FakeDualWriteConnector(emptyRequestId: true);
        using var vm = new DualWriteOpsViewModel(connector, Env, new Dialogs());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        var name = vm.LastLifecycleReceipt!.Targets[0].Name;
        vm.Maps[0].IsSelected = false;
        vm.Maps[1].IsSelected = true;
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Equal(1, connector.LastGateway!.StartCount);
        Assert.Contains(name, vm.ReadbackText);
        Assert.Contains("not proof", vm.ReadbackText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_or_drifted_post_does_not_publish_held_readback(bool dispose)
    {
        var current = Env();
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((verb, _, _) => verb == "GET" ? gate.Task : Task.FromResult(new ODataResponse(204, "OK", "", 0)));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: () => current) { Method = "PATCH" };
        await vm.SendCommand.ExecuteAsync(null);
        var read = vm.ReconcileCommand.ExecuteAsync(null);
        if (dispose) vm.Dispose(); else current = current with { Legal = "OTHER" };
        gate.SetResult(new ODataResponse(200, "OK", "STALE", 0));
        await read;
        Assert.DoesNotContain("STALE", vm.ReadbackText);
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public async Task Attached_post_receipt_and_readback_remain_visible_in_separate_measured_panels()
    {
        var client = new Client((verb, _, _) => verb == "GET" ? Task.FromResult(new ODataResponse(200, "OK", "current", 0)) : throw new InvalidOperationException());
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env) { Method = "PATCH", Path = "/data/X(1)" };
        var view = new PostBuilderView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();
        try
        {
            await vm.SendCommand.ExecuteAsync(null);
            await vm.ReconcileCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var receipt = view.FindControl<TextBlock>("ReceiptText")!;
            var readback = view.FindControl<TextBlock>("ReadbackText")!;
            var reconcile = view.FindControl<Button>("ReconcileButton")!;
            Assert.True(receipt.IsEffectivelyVisible);
            Assert.True(readback.IsEffectivelyVisible);
            Assert.True(receipt.Bounds.Height > 0);
            Assert.True(readback.Bounds.Height > 0);
            Assert.Contains("unknown", receipt.Text);
            Assert.Contains("does not prove", readback.Text);
            Assert.Equal("Read latest write state", reconcile.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Attached_ops_receipt_and_readback_do_not_replace_each_other()
    {
        using var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(emptyRequestId: true), Env, new Dialogs());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        var view = new DualWriteOpsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1300, Height = 900 };
        window.Show();
        try
        {
            await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
            await vm.ReconcileCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var receipt = view.FindControl<TextBlock>("ReceiptText")!;
            var readback = view.FindControl<TextBlock>("ReadbackText")!;
            var reconcile = view.FindControl<Button>("ReconcileButton")!;
            Assert.True(receipt.IsEffectivelyVisible && receipt.Bounds.Height > 0);
            Assert.True(readback.IsEffectivelyVisible && readback.Bounds.Height > 0);
            Assert.Contains("unconfirmed", receipt.Text);
            Assert.Contains("not proof", readback.Text);
            Assert.Equal("Read latest write state", reconcile.Content);
        }
        finally { window.Close(); }
    }

    private sealed class Gateway : IDualWriteGateway
    {
        public Func<CancellationToken, Task<DualWriteActionResponse>> Start { get; set; } = _ => Task.FromResult(new DualWriteActionResponse("r1", null));
        public Func<CancellationToken, Task<DualWriteRequestStatus>> Status { get; set; } = _ => Task.FromResult(new DualWriteRequestStatus("r1", "Completed", true, true, null));
        public int Starts { get; private set; }
        public int MapReads { get; private set; }
        public Task<DualWriteEnvironment> GetEnvironmentAsync(string id, CancellationToken ct = default) => Task.FromResult(new DualWriteEnvironment("cid", "name", id));
        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken ct = default)
        { MapReads++; return Task.FromResult<IReadOnlyList<DualWriteMap>>(new[] { new DualWriteMap("m1", "Captured Map", "Captured Map", "p1", "Running", null, Array.Empty<DualWriteTemplate>()) }); }
        public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken ct = default) { Starts++; return Start(ct); }
        public Task<DualWriteRequestStatus> GetStatusAsync(string id, CancellationToken ct = default) => Status(ct);
        public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string c, string p, string t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshTablesAsync(string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DualWriteConnectionSet> GetConnectionSetAsync(string c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ResetLinksAsync(string c, DualWriteConnectionSet s, IReadOnlyList<string> l, bool f, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ApplyIntegrationKeysAsync(string d, string c, IReadOnlyList<string> k, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Connector(Gateway gateway) : IDualWriteConnector
    {
        public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default) => Task.FromResult(new DualWriteSession(gateway, "captured-cid", "captured-name", env));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ops_changed_server_then_lost_or_incomplete_response_preserves_uncertainty(bool observedHeaders)
    {
        var changed = false;
        var gateway = new Gateway { Start = ct => {
            changed = true;
            if (observedHeaders) throw new DualWriteMutationCanceledException(new DualWriteMutationEvidence(true, 202, false, "IOException"), new OperationCanceledException(), ct);
            throw new InvalidOperationException("lost");
        } };
        var dialogs = new Dialogs();
        using var vm = new DualWriteOpsViewModel(new Connector(gateway), Env, dialogs);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        Assert.True(changed);
        var receipt = vm.LastLifecycleReceipt!;
        Assert.True(receipt.Unconfirmed);
        Assert.Equal(observedHeaders ? 202 : (int?)null, receipt.Observation.StatusCode);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Same(receipt, vm.LastLifecycleReceipt);
        Assert.Equal(1, gateway.Starts);
        Assert.Contains("not proof", vm.ReadbackText);
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        Assert.Contains("unconfirmed", dialogs.Requests.Last().Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ops_held_status_disposal_or_drift_discards_readback_and_sends_no_followon(bool dispose)
    {
        var current = Env();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<DualWriteRequestStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new Gateway { Status = _ => { entered.TrySetResult(); return gate.Task; } };
        using var vm = new DualWriteOpsViewModel(new Connector(gateway), () => current, new Dialogs(), pollInterval: TimeSpan.FromMilliseconds(1));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        var run = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (dispose) vm.Dispose(); else current = current with { Tenant = "other" };
        gate.TrySetResult(new DualWriteRequestStatus("r1", "STALE", true, true, null));
        await run;
        Assert.Null(vm.LastLifecycleReceipt!.TerminalState);
        Assert.Equal(1, gateway.MapReads);
        Assert.DoesNotContain("STALE", vm.Status);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Contains("disabled", vm.ReconcileReason);
        Assert.Equal(1, gateway.Starts);
    }

    [Fact]
    public async Task Ops_preconfirmation_cancel_and_direct_overlap_never_submit_or_steal_cancel()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new Dialogs { Gate = gate };
        var gateway = new Gateway();
        using var vm = new DualWriteOpsViewModel(new Connector(gateway), Env, dialogs);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        var run = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        try
        {
            await vm.RunActionCommand.ExecuteAsync(vm.StartAction);
            await vm.LoadCommand.ExecuteAsync(null);
            await vm.ReconcileCommand.ExecuteAsync(null);
            vm.RunActionCommand.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Single(dialogs.Requests);
            Assert.Equal(0, gateway.Starts);
            Assert.Contains("Not sent", vm.Status);
        }
        finally { gate.TrySetResult(true); await run; }
        Assert.Equal(0, gateway.Starts);
    }

    [Fact]
    public async Task Debug_cancel_after_applied_patch_leaves_remaining_projects_not_attempted()
    {
        var maps = Enumerable.Range(1,2).Select(i => new DualWriteMap($"m{i}", "Map", "Map", $"p{i}", "Stopped", null, Array.Empty<DualWriteTemplate>())).ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = false;
        var client = new Client(async (verb, _, ct) => {
            if (verb == "GET") return new ODataResponse(200,"OK","{\"value\":[{\"@odata.id\":\"https://fo.example/data/Config(1)\"}]}",0);
            changed = true; entered.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(ct);
            throw new InvalidOperationException();
        });
        using var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(maps), Env, new Dialogs(), odata: client, metadata: new Metadata());
        await vm.LoadCommand.ExecuteAsync(null);
        foreach (var row in vm.Maps) row.IsSelected = true;
        var run = vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        vm.EnableDebugForSelectedCommand.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(changed);
        Assert.True(vm.LastDebugReceipt!.Projects[0].Observation!.Unconfirmed);
        Assert.Null(vm.LastDebugReceipt.Projects[1].Observation);
        Assert.Single(client.Calls, c => c.Method == "PATCH");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ops_explicit_readback_discards_late_state_after_disposal_or_session_drift(bool dispose)
    {
        var current = Env();
        var gateway = new Gateway();
        using var vm = new DualWriteOpsViewModel(new Connector(gateway), () => current, new Dialogs(), pollInterval: TimeSpan.FromMilliseconds(1));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        var gate = new TaskCompletionSource<DualWriteRequestStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.Status = _ => gate.Task;
        var read = vm.ReconcileCommand.ExecuteAsync(null);
        if (dispose) vm.Dispose(); else current = current with { Legal = "OTHER" };
        gate.SetResult(new DualWriteRequestStatus("r1", "STALE", true, true, null));
        await read;
        Assert.DoesNotContain("STALE", vm.ReadbackText);
        Assert.Equal(1, gateway.Starts);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(201, false)]
    public async Task Successful_get_never_rewrites_5xx_or_incomplete_original_evidence(int code, bool complete)
    {
        var client = new Client((verb, _, _) => Task.FromResult(verb == "GET" ? new ODataResponse(200,"OK","current",0) :
            new ODataResponse(code,"Observed","",0) { DispatchStarted = true, BodyComplete = complete }));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env) { Method = "PATCH" };
        await vm.SendCommand.ExecuteAsync(null);
        var receipt = vm.LastReceipt;
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Same(receipt, vm.LastReceipt);
        Assert.True(vm.LastReceipt!.Observation.Unconfirmed);
        Assert.Contains("unconfirmed", vm.WriteReceiptText);
    }

    [Fact]
    public async Task Patch_readback_preserves_captured_endpoint_path_prefix()
    {
        var env = Env() with { Url = "https://fo.example/ProxyPath" };
        var client = new Client((_, _, _) => Task.FromResult(new ODataResponse(204,"OK","",0)));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: () => env) { Method = "PATCH", Path = "/data/X(1)" };
        await vm.SendCommand.ExecuteAsync(null);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Equal(("GET", "https://fo.example/ProxyPath/data/X(1)"), client.Calls.Last());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Drift_after_post_dispatch_retains_only_captured_acknowledgment_not_current_response(bool dispose)
    {
        var current = Env();
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, _, _) => gate.Task);
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: () => current);
        var send = vm.SendCommand.ExecuteAsync(null);
        if (dispose) vm.Dispose(); else current = current with { Url = "https://changed.example" };
        gate.SetResult(new ODataResponse(201,"Created","old response",0,
            new Dictionary<string,string> { ["Location"] = "https://fo.example/data/X(1)" }) { DispatchStarted = true });
        await send;
        Assert.Equal("https://fo.example", vm.LastReceipt!.Scope.Identity!.FoEndpoint);
        Assert.Equal(dispose ? null : (int?)201, vm.LastReceipt.Observation.StatusCode);
        Assert.Equal(dispose ? null : "https://fo.example/data/X(1)", vm.LastReceipt.Locator);
        if (!dispose) Assert.Contains("Context changed", vm.StatusText);
        Assert.DoesNotContain("old response", vm.ResponseBody);
        Assert.Contains("disabled", vm.ReconcileReason);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task Drift_after_debug_patch_preserves_ack_and_stops_before_next_project()
    {
        var current = Env();
        var maps = Enumerable.Range(1,2).Select(i => new DualWriteMap($"m{i}", "Map", "Map", $"p{i}", "Stopped", null, Array.Empty<DualWriteTemplate>())).ToArray();
        var client = new Client((verb, _, _) => {
            if (verb == "GET") return Task.FromResult(new ODataResponse(200,"OK","{\"value\":[{\"@odata.id\":\"https://fo.example/data/Config(1)\"}]}",0));
            current = current with { Tenant = "changed" };
            return Task.FromResult(new ODataResponse(204,"OK","",0) { DispatchStarted = true });
        });
        using var vm = new DualWriteOpsViewModel(new FakeDualWriteConnector(maps), () => current, new Dialogs(), odata: client, metadata: new Metadata());
        await vm.LoadCommand.ExecuteAsync(null);
        foreach (var row in vm.Maps) row.IsSelected = true;
        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);
        Assert.Equal(204, vm.LastDebugReceipt!.Projects[0].Observation!.StatusCode);
        Assert.Null(vm.LastDebugReceipt.Projects[1].Observation);
        Assert.Equal(2, client.Calls.Count);
        Assert.Contains("disabled", vm.ReconcileReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_ack_after_drift_retains_old_scope_without_polling_and_disposal_never_settles(bool dispose)
    {
        var current = Env();
        var gate = new TaskCompletionSource<DualWriteActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var polls = 0;
        var gateway = new Gateway { Start = _ => gate.Task, Status = _ => { polls++; throw new InvalidOperationException("No follow-on allowed"); } };
        using var vm = new DualWriteOpsViewModel(new Connector(gateway), () => current, new Dialogs());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;
        var run = vm.RunActionCommand.ExecuteAsync(vm.StopAction);
        if (dispose) vm.Dispose(); else current = current with { Tenant = "other" };
        gate.SetResult(new DualWriteActionResponse("captured-request", null) { Acknowledgment = new DualWriteMutationEvidence(true, 202, true) });
        await run;
        Assert.Equal(Env().Tenant, vm.LastLifecycleReceipt!.Scope.Identity!.Tenant);
        Assert.Equal(dispose ? null : "captured-request", vm.LastLifecycleReceipt.RequestId);
        Assert.Equal(dispose ? null : (int?)202, vm.LastLifecycleReceipt.Observation.StatusCode);
        Assert.Equal(0, polls);
        Assert.Equal(1, gateway.MapReads);
        if (!dispose) Assert.Contains("Context changed", vm.Status);
    }

    [Fact]
    public async Task A_readback_owner_rejects_writes_without_losing_cancellation_or_receipt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new Dialogs();
        var client = new Client(async (verb, _, ct) => {
            if (verb == "GET") { entered.TrySetResult(); await new TaskCompletionSource().Task.WaitAsync(ct); }
            return new ODataResponse(204,"OK","",0);
        });
        using var vm = new PostBuilderViewModel(client, dialogs: dialogs, activeEnv: Env) { Method = "PATCH" };
        await vm.SendCommand.ExecuteAsync(null);
        var receipt = vm.LastReceipt;
        var read = vm.ReconcileCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await vm.SendCommand.ExecuteAsync(null);
        await vm.ReconcileCommand.ExecuteAsync(null);
        Assert.Same(read, vm.ReconcileCommand.ExecutionTask);
        Assert.Same(receipt, vm.LastReceipt);
        vm.SendCancelCommand.Execute(null);
        await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(vm.ReconcileCommand.IsCancellationRequested);
        Assert.Single(dialogs.Requests);
        Assert.Equal(2, client.Calls.Count);
        Assert.Contains("cancelled", vm.ReadbackText);
    }

    [Fact]
    public async Task Known_local_auth_refusal_does_not_invent_an_observed_http_status()
    {
        var client = new Client((_, _, _) => Task.FromResult(new ODataResponse(401,"Unauthorized","auth refused locally",0) { DispatchStarted = false }));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.False(vm.LastReceipt!.Observation.DispatchStarted);
        Assert.Null(vm.LastReceipt.Observation.StatusCode);
        Assert.Contains("Not sent", vm.StatusText);
    }

    [Theory]
    [InlineData("http", "HTTP 401")]
    [InlineData("missing", "record was not returned")]
    [InlineData("transport", "HttpRequestException")]
    [InlineData("cancel", "cancelled")]
    public async Task Debug_project_read_failures_remain_distinct_from_the_not_sent_write(
        string mode,
        string expectedDiagnostic)
    {
        var client = new Client((verb, _, ct) =>
        {
            if (verb == "PATCH") throw new InvalidOperationException("PATCH must not run after a failed read.");
            return mode switch
            {
                "http" => Task.FromResult(new ODataResponse(401, "Unauthorized", "UNBOUNDED-BODY", 12)),
                "missing" => Task.FromResult(new ODataResponse(200, "OK", "{\"value\":[]}", 8)),
                "transport" => throw new HttpRequestException("socket reset while reading project configuration"),
                _ => throw new OperationCanceledException(ct)
            };
        });
        using var vm = new DualWriteOpsViewModel(
            new FakeDualWriteConnector(), Env, new Dialogs(), odata: client, metadata: new Metadata());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;

        await vm.EnableDebugForSelectedCommand.ExecuteAsync(null);

        var project = Assert.Single(vm.LastDebugReceipt!.Projects);
        Assert.False(project.Observation!.DispatchStarted);
        Assert.Contains(project.ProjectId, project.Summary);
        Assert.Contains("Not sent", project.Summary);
        Assert.Contains(expectedDiagnostic, project.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNBOUNDED-BODY", project.Summary);
        Assert.DoesNotContain(client.Calls, call => call.Method == "PATCH");
    }

    [Fact]
    public async Task Complete_gateway_rejection_keeps_bounded_UI_reason_without_tracing_it()
    {
        const string marker = "VISIBLE-GATEWAY-REJECTION-MARKER";
        using var trace = new TraceCapture();
        var gateway = new Gateway
        {
            Start = _ => throw new DualWriteGatewayException(
                $"Dual-write gateway request failed: 502 Bad Gateway. {marker}\r\ncontrol\u0001detail{new string('x', 500)}",
                HttpStatusCode.BadGateway)
            {
                Evidence = new DualWriteMutationEvidence(true, 502, BodyComplete: true)
            }
        };
        using var vm = new DualWriteOpsViewModel(new Connector(gateway), Env, new Dialogs());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Maps[0].IsSelected = true;

        await vm.RunActionCommand.ExecuteAsync(vm.StopAction);

        Assert.Equal(502, vm.LastLifecycleReceipt!.Observation.StatusCode);
        Assert.True(vm.LastLifecycleReceipt.Observation.Unconfirmed);
        Assert.InRange(vm.LastLifecycleReceipt.Diagnostic!.Length, 1, 321);
        Assert.DoesNotContain('\u0001', vm.LastLifecycleReceipt.Diagnostic);
        Assert.Contains(marker, vm.LastLifecycleReceipt.Summary);
        Assert.Contains(marker, vm.Status);
        Assert.Contains(vm.GatewayLog, entry => entry.Text.Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(marker, trace.Text);
        Assert.Contains("Mutation observation stopped", trace.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_receipt_keeps_observed_response_elapsed_time(bool cancelled)
    {
        var observed = new ODataResponse(503, "Service Unavailable", "", 321)
        {
            DispatchStarted = true,
            BodyComplete = false,
            BodyReadError = "IOException"
        };
        var client = new Client((_, _, ct) => cancelled
            ? throw new ODataWriteCanceledException(true, observed, new OperationCanceledException(ct), ct)
            : Task.FromResult(observed));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("321 ms", vm.WriteReceiptText);
        Assert.Contains("321 ms", vm.StatusText);
        Assert.True(vm.LastReceipt!.Observation.Unconfirmed);
    }

    [Fact]
    public async Task Post_failure_without_observed_response_does_not_invent_elapsed_HTTP_time()
    {
        var client = new Client((_, _, _) => throw new HttpRequestException("connection lost"));
        using var vm = new PostBuilderViewModel(client, dialogs: new Dialogs(), activeEnv: Env);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.DoesNotContain(" ms", vm.WriteReceiptText);
    }

    private sealed class TraceCapture : IDisposable
    {
        private readonly StringWriter _writer = new();
        private readonly TextWriterTraceListener _listener;
        public TraceCapture()
        {
            _listener = new TextWriterTraceListener(_writer);
            Trace.Listeners.Add(_listener);
        }
        public string Text { get { Trace.Flush(); return _writer.ToString(); } }
        public void Dispose()
        {
            Trace.Listeners.Remove(_listener);
            _listener.Dispose();
            _writer.Dispose();
        }
    }
}
