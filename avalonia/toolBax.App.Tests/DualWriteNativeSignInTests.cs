using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
