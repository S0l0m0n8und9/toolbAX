using FoToolbox.Core.Net;
using System.Net;
using System.Net.Http.Headers;

namespace FoToolbox.Tests;

public sealed class ReadRetryPolicyTests
{
    private static readonly Uri RequestUri = new("https://example.test/data");

    [Fact]
    public async Task SendAsync_RetriesTransientGetWithFreshRequest()
    {
        var firstContent = new TrackingContent();
        var finalContent = new TrackingContent();
        var handler = new ScriptedHandler(
            (_, _) => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, firstContent)),
            (_, _) => Task.FromResult(Response(HttpStatusCode.OK, finalContent)));
        using var client = new HttpClient(handler);
        var factoryCalls = 0;

        using var response = await ImmediatePolicy().SendAsync(
            client,
            () =>
            {
                factoryCalls++;
                return Get();
            },
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, factoryCalls);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotSame(handler.Requests[0], handler.Requests[1]);
        Assert.True(firstContent.IsDisposed);
        Assert.False(finalContent.IsDisposed);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task SendAsync_RetriesOnlyConfiguredTransientStatuses(HttpStatusCode status)
    {
        var handler = new ScriptedHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(status)),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);

        using var response = await ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendAsync_DoesNotRetryPermanentReadStatuses(HttpStatusCode status)
    {
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        using var client = new HttpClient(handler);

        using var response = await ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.Equal(status, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_ExhaustsConfiguredAttemptsAndReturnsLastResponse()
    {
        var contents = Enumerable.Range(0, 3).Select(_ => new TrackingContent()).ToArray();
        var steps = contents
            .Select<TrackingContent, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(
                content => (_, _) => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, content)))
            .ToArray();
        var handler = new ScriptedHandler(steps);
        using var client = new HttpClient(handler);

        using var response = await ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
        Assert.True(contents[0].IsDisposed);
        Assert.True(contents[1].IsDisposed);
        Assert.False(contents[2].IsDisposed);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.Unknown)]
    public async Task SendAsync_RetriesSelectedTransportFailures(HttpRequestError error)
    {
        var handler = new ScriptedHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException(error, "transient")),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);

        using var response = await ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.UserAuthenticationError)]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded)]
    [InlineData(HttpRequestError.HttpProtocolError)]
    public async Task SendAsync_DoesNotRetryExcludedTransportFailures(HttpRequestError error)
    {
        var expected = new HttpRequestException(error, "permanent");
        var handler = new ScriptedHandler((_, _) => Task.FromException<HttpResponseMessage>(expected));
        using var client = new HttpClient(handler);

        var actual = await Assert.ThrowsAsync<HttpRequestException>(() => ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_RetriesTimeoutShapedCancellationWhenCallerAndBudgetRemainLive()
    {
        var handler = new ScriptedHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new OperationCanceledException("client timeout")),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);

        using var response = await ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_AppliesExponentialBackoffUsingVirtualTime()
    {
        var clock = new ManualTimeProvider();
        var handler = new ScriptedHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        var pending = new ReadRetryPolicy(timeProvider: clock).SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        await WaitForAsync(() => handler.Requests.Count == 1);
        clock.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Single(handler.Requests);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await WaitForAsync(() => handler.Requests.Count == 2);
        clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Equal(2, handler.Requests.Count);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        using var response = await pending;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendAsync_HonorsRetryAfterDeltaAndDateAsMinimum(bool useDate)
    {
        var clock = new ManualTimeProvider();
        var first = new HttpResponseMessage((HttpStatusCode)429);
        first.Headers.RetryAfter = useDate
            ? new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(3))
            : new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        var handler = new ScriptedHandler(
            (_, _) => Task.FromResult(first),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        var pending = new ReadRetryPolicy(timeProvider: clock).SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        await WaitForAsync(() => handler.Requests.Count == 1);
        clock.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.Single(handler.Requests);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        using var response = await pending;
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_ReturnsLastResponseWhenValidRetryAfterCannotFitBudget()
    {
        var first = new HttpResponseMessage((HttpStatusCode)429);
        first.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(31));
        var handler = new ScriptedHandler((_, _) => Task.FromResult(first));
        using var client = new HttpClient(handler);

        var response = await new ReadRetryPolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.Same(first, response);
        Assert.Single(handler.Requests);
        response.Dispose();
    }

    [Fact]
    public async Task SendAsync_InvalidRetryAfterFallsBackToNormalBackoff()
    {
        var clock = new ManualTimeProvider();
        var first = new HttpResponseMessage((HttpStatusCode)429);
        first.Headers.TryAddWithoutValidation("Retry-After", "not-a-delay");
        var handler = new ScriptedHandler(
            (_, _) => Task.FromResult(first),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        var pending = new ReadRetryPolicy(timeProvider: clock).SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        await WaitForAsync(() => handler.Requests.Count == 1);
        clock.Advance(TimeSpan.FromMilliseconds(250));

        using var response = await pending;
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_BudgetExpiryDuringIgnoringSendIsTimeoutExceptionAndCompletesPromptly()
    {
        var clock = new ManualTimeProvider();
        var never = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHandler((_, _) => never.Task);
        using var client = new HttpClient(handler);
        var pending = new ReadRetryPolicy(overallBudget: TimeSpan.FromSeconds(1), timeProvider: clock).SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        await WaitForAsync(() => handler.Requests.Count == 1);
        clock.Advance(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        never.SetException(new InvalidOperationException("late fault"));
    }

    [Fact]
    public async Task SendAsync_BudgetExpiryDuringDelayIsTimeoutExceptionAndDisposesIntermediateResponse()
    {
        var clock = new ManualTimeProvider();
        var content = new TrackingContent();
        var handler = new ScriptedHandler((_, _) => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, content)));
        using var client = new HttpClient(handler);
        var pending = new ReadRetryPolicy(
            overallBudget: TimeSpan.FromSeconds(1),
            initialBackoff: TimeSpan.FromMilliseconds(750),
            timeProvider: clock).SendAsync(
                client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        await WaitForAsync(() => handler.Requests.Count == 1);
        clock.Advance(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(content.IsDisposed);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_InitialFactoryTimeCountsAgainstBudget()
    {
        var clock = new ManualTimeProvider();
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<TimeoutException>(() => new ReadRetryPolicy(
            overallBudget: TimeSpan.FromSeconds(1), timeProvider: clock).SendAsync(
                client,
                () =>
                {
                    clock.Advance(TimeSpan.FromSeconds(1));
                    return Get();
                },
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_PreAttemptTimeCountsAgainstBudget()
    {
        var clock = new ManualTimeProvider();
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<TimeoutException>(() => new ReadRetryPolicy(
            overallBudget: TimeSpan.FromSeconds(1), timeProvider: clock).SendAsync(
                client,
                Get,
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken.None,
                () => clock.Advance(TimeSpan.FromSeconds(1))));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_CallerCancellationBeforeSendPropagatesWithoutDispatch()
    {
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_CallerCancellationDuringIgnoringSendPropagatesAndCleansUpLateResponse()
    {
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestContent = new TrackingContent();
        var responseContent = new TrackingContent();
        var handler = new ScriptedHandler((_, _) => late.Task);
        using var client = new HttpClient(handler);
        using var caller = new CancellationTokenSource();
        var pending = ImmediatePolicy().SendAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, RequestUri) { Content = requestContent },
            HttpCompletionOption.ResponseHeadersRead,
            caller.Token);

        await WaitForAsync(() => handler.Requests.Count == 1);
        caller.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.False(requestContent.IsDisposed);

        late.SetResult(Response(HttpStatusCode.OK, responseContent));
        await WaitForAsync(() => requestContent.IsDisposed && responseContent.IsDisposed);
    }

    [Fact]
    public async Task SendAsync_CancellationCompletionRaceDisposesCompletedResponse()
    {
        using var caller = new CancellationTokenSource();
        var responseContent = new TrackingContent();
        var handler = new ScriptedHandler((_, _) =>
        {
            caller.Cancel();
            return Task.FromResult(Response(HttpStatusCode.OK, responseContent));
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, caller.Token));

        await WaitForAsync(() => responseContent.IsDisposed);
    }

    [Fact]
    public async Task SendAsync_BudgetCompletionRaceDisposesCompletedResponse()
    {
        var clock = new ManualTimeProvider();
        var responseContent = new TrackingContent();
        var handler = new ScriptedHandler((_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            return Task.FromResult(Response(HttpStatusCode.OK, responseContent));
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<TimeoutException>(() => new ReadRetryPolicy(
            overallBudget: TimeSpan.FromSeconds(1), timeProvider: clock).SendAsync(
                client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None));

        await WaitForAsync(() => responseContent.IsDisposed);
    }

    [Fact]
    public async Task SendAsync_CancellationCompletionRaceObservesCompletedFault()
    {
        using var caller = new CancellationTokenSource();
        var requestContent = new TrackingContent();
        var handler = new ScriptedHandler((_, _) =>
        {
            caller.Cancel();
            return Task.FromException<HttpResponseMessage>(new InvalidOperationException("completed late fault"));
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImmediatePolicy().SendAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, RequestUri) { Content = requestContent },
            HttpCompletionOption.ResponseHeadersRead,
            caller.Token));

        await WaitForAsync(() => requestContent.IsDisposed);
    }

    [Fact]
    public async Task SendAsync_CallerCancellationDuringIgnoringSendObservesLateFaultAndDisposesRequest()
    {
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestContent = new TrackingContent();
        var handler = new ScriptedHandler((_, _) => late.Task);
        using var client = new HttpClient(handler);
        using var caller = new CancellationTokenSource();
        var pending = ImmediatePolicy().SendAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, RequestUri) { Content = requestContent },
            HttpCompletionOption.ResponseHeadersRead,
            caller.Token);

        await WaitForAsync(() => handler.Requests.Count == 1);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        late.SetException(new HttpRequestException(HttpRequestError.ConnectionError, "late"));
        await WaitForAsync(() => requestContent.IsDisposed);
    }

    [Fact]
    public async Task SendAsync_CallerCancellationDuringDelayPropagatesAndDisposesIntermediateResponse()
    {
        var clock = new ManualTimeProvider();
        var content = new TrackingContent();
        var handler = new ScriptedHandler((_, _) => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, content)));
        using var client = new HttpClient(handler);
        using var caller = new CancellationTokenSource();
        var pending = new ReadRetryPolicy(initialBackoff: TimeSpan.FromSeconds(10), timeProvider: clock).SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, caller.Token);

        await WaitForAsync(() => handler.Requests.Count == 1);
        caller.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.True(content.IsDisposed);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_RejectsReusedRequestBeforeSecondDispatch()
    {
        var shared = Get();
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ImmediatePolicy().SendAsync(
            client, () => shared, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None));

        Assert.Contains("fresh request", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendAsync_RejectsMethodOrUriDriftBeforeSecondDispatch(bool changeMethod)
    {
        var calls = 0;
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ImmediatePolicy().SendAsync(
            client,
            () =>
            {
                calls++;
                return calls == 1
                    ? Get()
                    : new HttpRequestMessage(
                        changeMethod ? HttpMethod.Post : HttpMethod.Get,
                        changeMethod ? RequestUri : new Uri("https://example.test/DATA"));
            },
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_PreAttemptRefusalRunsBeforeEverySendAndIsNotRetried()
    {
        var guardCalls = 0;
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<ScopeRefusedException>(() => ImmediatePolicy().SendAsync(
            client,
            Get,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None,
            () =>
            {
                guardCalls++;
                if (guardCalls == 2)
                    throw new ScopeRefusedException();
            }));

        Assert.Equal(2, guardCalls);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_FactoryFailureIsNotRetried()
    {
        var factoryCalls = 0;
        var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<FactoryFailedException>(() => ImmediatePolicy().SendAsync(
            client,
            () => ++factoryCalls == 1 ? Get() : throw new FactoryFailedException(),
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None));

        Assert.Equal(2, factoryCalls);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task SendAsync_NonGetIsSentExactlyOnceWithoutReplay(string method)
    {
        HttpRequestMessage? observed = null;
        var handler = new ScriptedHandler((request, _) =>
        {
            observed = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var client = new HttpClient(handler);
        var created = new HttpRequestMessage(new HttpMethod(method), RequestUri);
        var factoryCalls = 0;
        var guardCalls = 0;

        using var response = await new ReadRetryPolicy().SendAsync(
            client,
            () =>
            {
                factoryCalls++;
                return created;
            },
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None,
            () => guardCalls++);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Same(created, observed);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, guardCalls);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_PreservesResponseHeadersReadCompletionOption()
    {
        var content = new SerializationProbeContent();
        var handler = new ScriptedHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, content)));
        using var client = new HttpClient(handler);

        using var response = await ImmediatePolicy().SendAsync(
            client, Get, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.False(content.SerializationStarted);
    }

    private static ReadRetryPolicy ImmediatePolicy() => new(initialBackoff: TimeSpan.Zero);

    private static HttpRequestMessage Get() => new(HttpMethod.Get, RequestUri);

    private static HttpResponseMessage Response(HttpStatusCode status, HttpContent content) =>
        new(status) { Content = content };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The test condition was not reached.");
            await Task.Delay(10);
        }
    }

    private sealed class ScriptedHandler(
        params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps) : HttpMessageHandler
    {
        private int _index;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var index = Interlocked.Increment(ref _index) - 1;
            return steps[Math.Min(index, steps.Length - 1)](request, cancellationToken);
        }
    }

    private sealed class TrackingContent : ByteArrayContent
    {
        public TrackingContent() : base([]) { }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class SerializationProbeContent : HttpContent
    {
        public bool SerializationStarted { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            SerializationStarted = true;
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ScheduledTimer> _timers = [];
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
                return DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        }

        public override long GetTimestamp()
        {
            lock (_sync)
                return _ticks;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            if (amount < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(amount));

            long target;
            lock (_sync)
                target = checked(_ticks + amount.Ticks);

            while (true)
            {
                ScheduledTimer? scheduled;
                lock (_sync)
                {
                    scheduled = _timers
                        .Where(item => item.Timer.IsCurrent(item.Generation))
                        .OrderBy(item => item.DueAt)
                        .FirstOrDefault(item => item.DueAt <= target);
                    if (scheduled is null)
                    {
                        _ticks = target;
                        return;
                    }

                    _timers.Remove(scheduled);
                    _ticks = scheduled.DueAt;
                    scheduled.Timer.MarkFired(scheduled.Generation);
                }

                scheduled.Timer.Invoke();
            }
        }

        private bool Change(ManualTimer timer, TimeSpan dueTime)
        {
            lock (_sync)
            {
                var generation = timer.NextGeneration();
                if (dueTime != Timeout.InfiniteTimeSpan)
                    _timers.Add(new ScheduledTimer(timer, generation, checked(_ticks + dueTime.Ticks)));
                return true;
            }
        }

        private void Dispose(ManualTimer timer)
        {
            lock (_sync)
                timer.MarkDisposed();
        }

        private sealed record ScheduledTimer(ManualTimer Timer, int Generation, long DueAt);

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private int _generation;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => owner.Change(this, dueTime);

            public void Dispose() => owner.Dispose(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public int NextGeneration() => ++_generation;
            public bool IsCurrent(int generation) => !_disposed && generation == _generation;
            public void MarkFired(int generation)
            {
                if (generation == _generation)
                    _generation++;
            }
            public void MarkDisposed()
            {
                _disposed = true;
                _generation++;
            }
            public void Invoke() => callback(state);
        }
    }

    private sealed class ScopeRefusedException : Exception;
    private sealed class FactoryFailedException : Exception;
}
