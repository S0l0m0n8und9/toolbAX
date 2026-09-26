using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FoToolbox.Core.DualWrite.Auth;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class DualWriteNativeSignInTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private const string TokenEndpoint = "https://login.microsoftonline.com/77777777-7777-7777-7777-777777777777/oauth2/v2.0/token";
    private const string Gateway = "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version";

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }

    private sealed class GatedUiStream(byte[] bytes, long restorePosition) : MemoryStream(bytes)
    {
        private bool _readStarted;
        private int _readCalls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RestoreOnUiThread { get; private set; }
        public override long Position
        {
            get => base.Position;
            set
            {
                if (_readStarted && value == restorePosition)
                    RestoreOnUiThread = Dispatcher.UIThread.CheckAccess();
                base.Position = value;
            }
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _readStarted = true;
            if (Interlocked.Increment(ref _readCalls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task;
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class CoordinatedWindow : Window
    {
        private readonly DualWriteClosePreparationCoordinator _coordinator;
        private readonly Window? _owner;
        private bool _resumeOwnerClose;
        public bool PhysicalCloseOnUiThread { get; private set; }
        public int ClosedCount { get; private set; }

        public CoordinatedWindow(Task preparation, Window? owner = null)
        {
            _owner = owner;
            _coordinator = new DualWriteClosePreparationCoordinator(
                action => Dispatcher.UIThread.Post(action),
                () =>
                {
                    PhysicalCloseOnUiThread = Dispatcher.UIThread.CheckAccess();
                    Close();
                });
            _coordinator.TrackPreparation(preparation);
            Closing += (_, e) =>
            {
                var ownerIsClosing = e.CloseReason == WindowCloseReason.OwnerWindowClosing;
                var shutdownCannotWait = e.CloseReason is WindowCloseReason.ApplicationShutdown
                    or WindowCloseReason.OSShutdown;
                if (_coordinator.RequestClose(shutdownCannotWait) == DualWriteCloseDecision.DeferAndHide)
                {
                    _resumeOwnerClose |= ownerIsClosing;
                    e.Cancel = true;
                    if (IsVisible) Hide();
                }
            };
            Closed += (_, _) =>
            {
                ClosedCount++;
                _coordinator.MarkClosed();
                if (_resumeOwnerClose && _owner is { } owner)
                {
                    _resumeOwnerClose = false;
                    Dispatcher.UIThread.Post(owner.Close);
                }
            };
        }

        public Task DrainAsync() => _coordinator.DrainPreparationAsync();
    }

    private sealed class NativeHostLifecycleProbe : NativeControlHost
    {
        public int AttachedCount { get; private set; }
        public int DetachedCount { get; private set; }
        public int DestroyedCount { get; private set; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            AttachedCount++;
            base.OnAttachedToVisualTree(e);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            DetachedCount++;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            DestroyedCount++;
            base.DestroyNativeControlCore(control);
        }
    }

    private sealed class CoordinatedAttempt : IDualWriteSignInAttempt
    {
        private readonly TaskCompletionSource<DualWriteSignInResult?> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _clear = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly DualWriteClosePreparationCoordinator _coordinator;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ResultCompleted => _result.Task.IsCompleted;
        public int PhysicalCloses { get; private set; }
        public int Navigations { get; private set; }
        public bool ControllerDestroyed { get; private set; }

        public CoordinatedAttempt()
        {
            _coordinator = new DualWriteClosePreparationCoordinator(action => action(), PhysicalClose);
            var preparation = DualWriteBrowserPreparation.PrepareAndNavigateAsync(
                () => _clear.Task, () => Navigations++, () => _coordinator.IsInactive);
            _coordinator.TrackPreparation(preparation);
        }

        public async Task<DualWriteSignInResult?> RunAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            return await _result.Task;
        }

        public void UserClose()
        {
            _coordinator.Deactivate();
            _result.TrySetResult(null);
            if (_coordinator.RequestClose() == DualWriteCloseDecision.AllowPhysicalClose)
                PhysicalClose();
        }

        public void Cancel() => UserClose();
        public Task DrainPreparationAsync() => _coordinator.DrainPreparationAsync();

        public void CompleteClear()
        {
            // Models the reviewed WebView2 failure: once controller teardown occurs, this clear can no
            // longer report completion. The coordinator must therefore keep the controller alive first.
            if (!ControllerDestroyed) _clear.TrySetResult();
        }

        private void PhysicalClose()
        {
            if (ControllerDestroyed) return;
            ControllerDestroyed = true;
            PhysicalCloses++;
            _coordinator.MarkClosed();
        }
    }

    [AvaloniaFact]
    public async Task Asynchronous_borrowed_stream_keeps_native_response_and_restore_on_UI_context()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var form = $"client_id={DualWriteAuthConstants.ClientId}&grant_type=authorization_code&scope=" +
                   Uri.EscapeDataString(DualWriteAuthConstants.Scope) + "&code=code";
        const long originalPosition = 6;
        var request = new GatedUiStream(Encoding.UTF8.GetBytes(form), originalPosition) { Position = originalPosition };
        var responseDelegateOnUi = false;
        var operation = DualWriteCommittedResponseReader.ReadTokenExchangeAsync(
            TokenEndpoint, "POST", "application/x-www-form-urlencoded", request, 200,
            () =>
            {
                responseDelegateOnUi = Dispatcher.UIThread.CheckAccess();
                return Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(
                    "{\"access_token\":\"opaque\",\"token_type\":\"Bearer\",\"expires_in\":3600}")));
            },
            TestContext.Current.CancellationToken);
        await request.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        request.Release.TrySetResult();

        Assert.NotNull(await operation);
        Assert.True(responseDelegateOnUi);
        Assert.True(request.RestoreOnUiThread);
        Assert.Equal(originalPosition, request.Position);
    }

    private sealed class RecordingResolver : IDualWriteTenantResolver
    {
        public string? Domain { get; private set; }
        public Task<Guid?> ResolveAsync(string domain, CancellationToken cancellationToken)
        {
            Domain = domain;
            return Task.FromResult<Guid?>(Tenant);
        }
    }

    [Fact]
    public async Task Native_capture_uses_the_immutable_profile_tenant_constraint()
    {
        var profile = new EnvProfile(
            "id", "Configured", "https://fo.example", "contoso.example", "USMF", "Tier 2", EnvStatus.Connected);
        var resolver = new RecordingResolver();
        var capture = DualWriteNativeCapture.Create(profile, resolver, () => DateTimeOffset.UtcNow);
        var form = $"client_id={DualWriteAuthConstants.ClientId}&grant_type=authorization_code&scope=" +
                   Uri.EscapeDataString(DualWriteAuthConstants.Scope) + "&code=code";
        var response = "{\"access_token\":\"opaque\",\"refresh_token\":\"refresh\",\"token_type\":\"Bearer\",\"expires_in\":3600}";

        Assert.False(await capture.ObserveTokenExchangeAsync(new DualWriteTokenExchangeObservation(
            new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token"),
            HttpMethod.Post,
            "application/x-www-form-urlencoded",
            form,
            200,
            response), CancellationToken.None));
        Assert.Equal("contoso.example", resolver.Domain);
    }

    [Fact]
    public async Task Committed_token_exchange_reads_bounded_streams_restores_borrowed_position_and_owns_response()
    {
        var form = $"client_id={DualWriteAuthConstants.ClientId}&grant_type=authorization_code&scope=" +
                   Uri.EscapeDataString(DualWriteAuthConstants.Scope) + "&code=code";
        var request = new MemoryStream(Encoding.UTF8.GetBytes(form));
        request.Position = 6;
        var originalPosition = request.Position;
        var response = new TrackingStream(Encoding.UTF8.GetBytes(
            "{\"access_token\":\"opaque\",\"refresh_token\":\"refresh\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
        var responseOpened = false;

        var observation = await DualWriteCommittedResponseReader.ReadTokenExchangeAsync(
            TokenEndpoint, "POST", "application/x-www-form-urlencoded; charset=utf-8", request,
            200, () =>
            {
                Assert.Equal(originalPosition, request.Position); // request read/restore precedes response access
                responseOpened = true;
                return Task.FromResult<Stream?>(response);
            }, CancellationToken.None);

        Assert.NotNull(observation);
        Assert.Equal(form, observation!.RequestForm);
        Assert.Equal(originalPosition, request.Position);
        Assert.True(request.CanRead);
        Assert.True(responseOpened);
        Assert.True(response.WasDisposed);
        Assert.DoesNotContain("opaque", DualWriteCommittedResponseReader.IncompleteDiagnostic);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Oversized_or_missing_committed_body_stays_incomplete(bool oversizedRequest, bool oversizedResponse)
    {
        var requestBytes = Encoding.UTF8.GetBytes(oversizedRequest
            ? new string('r', DualWriteCommittedResponseReader.MaximumBodyBytes + 1)
            : "client_id=x");
        var responseBytes = Encoding.UTF8.GetBytes(oversizedResponse
            ? new string('s', DualWriteCommittedResponseReader.MaximumBodyBytes + 1)
            : "{}");

        var result = await DualWriteCommittedResponseReader.ReadTokenExchangeAsync(
            TokenEndpoint, "POST", "application/x-www-form-urlencoded", new MemoryStream(requestBytes),
            200, () => Task.FromResult<Stream?>(new MemoryStream(responseBytes)), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Unrelated_traffic_is_ignored_then_valid_committed_observations_complete_capture()
    {
        var unrelated = await DualWriteCommittedResponseReader.ReadTokenExchangeAsync(
            "https://graph.microsoft.com/v1.0/me", "POST", "application/json", new MemoryStream(),
            200, () => Task.FromResult<Stream?>(new MemoryStream()), CancellationToken.None);
        Assert.Null(unrelated);

        var capture = new DualWriteSignInCapture(Tenant.ToString("D"));
        var form = $"client_id={DualWriteAuthConstants.ClientId}&grant_type=authorization_code&scope=" +
                   Uri.EscapeDataString(DualWriteAuthConstants.Scope) + "&code=code";
        var response = "{\"access_token\":\"opaque\",\"refresh_token\":\"refresh\",\"token_type\":\"Bearer\",\"expires_in\":3600}";
        var token = await DualWriteCommittedResponseReader.ReadTokenExchangeAsync(
            TokenEndpoint, "POST", "application/x-www-form-urlencoded",
            new MemoryStream(Encoding.UTF8.GetBytes(form)), 200,
            () => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(response))), CancellationToken.None);
        Assert.True(await capture.ObserveTokenExchangeAsync(token!, CancellationToken.None));

        var gateway = DualWriteCommittedResponseReader.CreateGatewayObservation(Gateway, "Bearer opaque", 200);
        Assert.NotNull(gateway);
        Assert.True(capture.ObserveGatewayResponse(gateway!));
        Assert.NotNull(capture.Result);
    }

    [Fact]
    public void Reset_policy_preserves_cookies_normally_and_clears_them_for_switch_account()
    {
        var normal = DualWriteBrowserPreparation.ResetKinds(switchAccount: false);
        var switched = DualWriteBrowserPreparation.ResetKinds(switchAccount: true);

        Assert.True(normal.HasFlag(DualWriteBrowserDataKinds.DomStorage));
        Assert.True(normal.HasFlag(DualWriteBrowserDataKinds.DiskCache));
        Assert.False(normal.HasFlag(DualWriteBrowserDataKinds.Cookies));
        Assert.True(switched.HasFlag(DualWriteBrowserDataKinds.DomStorage));
        Assert.True(switched.HasFlag(DualWriteBrowserDataKinds.DiskCache));
        Assert.True(switched.HasFlag(DualWriteBrowserDataKinds.Cookies));
    }

    [Fact]
    public async Task Failed_or_cancelled_profile_reset_never_navigates()
    {
        var navigations = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DualWriteBrowserPreparation.PrepareAndNavigateAsync(
                () => Task.FromException(new InvalidOperationException("reset failed")),
                () => navigations++,
                () => false));
        Assert.Equal(0, navigations);

        var clear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var preparation = DualWriteBrowserPreparation.PrepareAndNavigateAsync(
            () => clear.Task,
            () => navigations++,
            () => cancelled);
        cancelled = true;
        clear.TrySetResult();
        await preparation;
        Assert.Equal(0, navigations);
    }

    [Fact]
    public async Task Close_waits_for_uninterruptible_preparation_before_physical_teardown()
    {
        var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalCloses = 0;
        var controllerDestroyed = false;
        var coordinator = new DualWriteClosePreparationCoordinator(action => action(), () =>
        {
            controllerDestroyed = true;
            physicalCloses++;
        });
        coordinator.TrackPreparation(preparation.Task);

        var decision = coordinator.RequestClose();

        Assert.True(coordinator.IsInactive);
        Assert.Equal(DualWriteCloseDecision.DeferAndHide, decision);
        Assert.Equal(0, physicalCloses);
        if (!controllerDestroyed) preparation.TrySetResult();
        await coordinator.DrainPreparationAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, physicalCloses);
    }

    [Fact]
    public async Task Queued_sign_in_starts_only_after_closed_attempt_safely_drains()
    {
        var sequencer = new DualWriteSignInSequencer();
        var first = new CoordinatedAttempt();
        var firstRun = sequencer.RunAsync(() => first, CancellationToken.None);
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        first.UserClose();
        Assert.True(first.ResultCompleted);
        Assert.False(first.ControllerDestroyed);
        Assert.Equal(0, first.Navigations);

        var second = new FakeAttempt();
        var secondCreated = 0;
        var secondRun = sequencer.RunAsync(() => { secondCreated++; return second; }, CancellationToken.None);
        await Task.Yield();
        Assert.Equal(0, secondCreated);

        first.CompleteClear();
        await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, first.PhysicalCloses);
        Assert.Equal(0, first.Navigations);
        second.Complete(null);
        Assert.Null(await firstRun);
        await secondRun;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Faulted_or_cancelled_preparation_closes_once_and_duplicate_close_is_idempotent(bool cancel)
    {
        var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalCloses = 0;
        var coordinator = new DualWriteClosePreparationCoordinator(action => action(), () => physicalCloses++);
        coordinator.TrackPreparation(preparation.Task);

        Assert.Equal(DualWriteCloseDecision.DeferAndHide, coordinator.RequestClose());
        Assert.Equal(DualWriteCloseDecision.DeferAndHide, coordinator.RequestClose());
        if (cancel) preparation.TrySetCanceled(TestContext.Current.CancellationToken);
        else preparation.TrySetException(new InvalidOperationException("clear failed"));

        await coordinator.DrainPreparationAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, physicalCloses);
        Assert.Equal(DualWriteCloseDecision.AllowPhysicalClose, coordinator.RequestClose());
        Assert.Equal(1, physicalCloses);
    }

    [Fact]
    public void Destroyed_host_rejects_and_closes_a_late_created_resource_without_publication()
    {
        var lifetime = new DualWriteNativeHostLifetime();
        var teardowns = 0;
        var publications = 0;
        var lateCloses = 0;

        lifetime.Destroy(() => teardowns++);
        var accepted = lifetime.TryPublish(() => publications++);
        if (!accepted) lateCloses++;
        lifetime.Destroy(() => teardowns++);

        Assert.False(accepted);
        Assert.Equal(0, publications);
        Assert.Equal(1, lateCloses);
        Assert.Equal(1, teardowns);
    }

    [AvaloniaFact]
    public async Task Headless_close_hides_until_preparation_settles_then_closes_on_UI_context()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Window();
        owner.Show();
        var child = new CoordinatedWindow(preparation.Task, owner);
        var dialog = child.ShowDialog(owner);

        child.Close();

        Assert.False(child.IsVisible);
        Assert.Equal(0, child.ClosedCount);
        preparation.TrySetResult();
        await dialog.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await child.DrainAsync();
        Assert.Equal(1, child.ClosedCount);
        Assert.True(child.PhysicalCloseOnUiThread);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Owner_close_is_not_cancelled_by_child_with_outstanding_preparation()
    {
        var ownerClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Window();
        owner.Closed += (_, _) => ownerClosed.TrySetResult();
        owner.Show();
        var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = new CoordinatedWindow(preparation.Task, owner);
        var dialog = child.ShowDialog(owner);

        owner.Close();

        Assert.True(owner.IsVisible);
        Assert.False(child.IsVisible);
        preparation.TrySetResult();
        await dialog.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await child.DrainAsync();
        await ownerClosed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, child.ClosedCount);
    }

    [AvaloniaFact]
    public void Hiding_window_keeps_native_host_attached_and_does_not_destroy_it()
    {
        var host = new NativeHostLifecycleProbe();
        var window = new Window { Content = host };
        window.Show();
        Assert.Equal(1, host.AttachedCount);
        Assert.True(host.IsAttachedToVisualTree());

        window.Hide();

        Assert.Equal(0, host.DetachedCount);
        Assert.Equal(0, host.DestroyedCount);
        Assert.True(host.IsAttachedToVisualTree());
        window.Close();
    }

    [Fact]
    public async Task Sign_in_attempts_are_serialized_and_queued_cancellation_creates_no_attempt()
    {
        var sequencer = new DualWriteSignInSequencer();
        var first = new FakeAttempt();
        var secondCreated = 0;
        var firstRun = sequencer.RunAsync(() => first, CancellationToken.None);
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var queuedCancellation = new CancellationTokenSource();
        var queued = sequencer.RunAsync(() => { secondCreated++; return new FakeAttempt(); }, queuedCancellation.Token);
        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(0, secondCreated);

        first.Complete(null);
        await firstRun;
    }

    [Fact]
    public async Task Cancelled_uninterruptible_preparation_is_drained_before_next_attempt_starts()
    {
        var sequencer = new DualWriteSignInSequencer();
        var first = new FakeAttempt { FinishRunOnCancel = true };
        using var cancellation = new CancellationTokenSource();
        var firstRun = sequencer.RunAsync(() => first, cancellation.Token);
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await first.RunFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var second = new FakeAttempt();
        var secondCreated = 0;
        var secondRun = sequencer.RunAsync(() => { secondCreated++; return second; }, CancellationToken.None);
        await Task.Yield();
        Assert.Equal(0, secondCreated);

        first.ReleasePreparation();
        await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, secondCreated);
        second.Complete(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRun);
        await secondRun;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cleanup_failure_releases_gate_for_a_later_sign_in(bool cancelThrows)
    {
        var sequencer = new DualWriteSignInSequencer();
        var first = new FakeAttempt { CancelThrows = cancelThrows, DrainThrows = !cancelThrows };
        var firstRun = sequencer.RunAsync(() => first, CancellationToken.None);
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        first.Complete(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstRun);

        var second = new FakeAttempt();
        var secondRun = sequencer.RunAsync(() => second, CancellationToken.None);
        await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        second.Complete(null);
        await secondRun;
    }

    private sealed class FakeAttempt : IDualWriteSignInAttempt
    {
        private readonly TaskCompletionSource<DualWriteSignInResult?> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _preparation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RunFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FinishRunOnCancel { get; init; }
        public bool CancelThrows { get; init; }
        public bool DrainThrows { get; init; }

        public async Task<DualWriteSignInResult?> RunAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            using var registration = cancellationToken.Register(() =>
            {
                if (FinishRunOnCancel) _result.TrySetCanceled(cancellationToken);
            });
            try { return await _result.Task; }
            finally { RunFinished.TrySetResult(); }
        }
        public void Cancel()
        {
            if (CancelThrows) throw new InvalidOperationException("cancel cleanup failed");
        }
        public async Task DrainPreparationAsync()
        {
            await _preparation.Task;
            if (DrainThrows) throw new InvalidOperationException("drain cleanup failed");
        }
        public void Complete(DualWriteSignInResult? result) { _preparation.TrySetResult(); _result.TrySetResult(result); }
        public void ReleasePreparation() => _preparation.TrySetResult();
    }
}
