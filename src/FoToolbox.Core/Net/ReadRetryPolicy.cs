using System.Net;

namespace FoToolbox.Core.Net;

/// <summary>
/// Applies a bounded retry policy to safe HTTP reads. The request factory must return a new
/// request for every attempt. Responses returned from this policy are owned by the caller.
/// </summary>
public sealed class ReadRetryPolicy
{
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultMaximumBackoff = TimeSpan.FromSeconds(2);

    private readonly int _maximumAttempts;
    private readonly TimeSpan _overallBudget;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maximumBackoff;
    private readonly TimeProvider _timeProvider;

    public ReadRetryPolicy(
        int maximumAttempts = 3,
        TimeSpan? overallBudget = null,
        TimeSpan? initialBackoff = null,
        TimeSpan? maximumBackoff = null,
        TimeProvider? timeProvider = null)
    {
        if (maximumAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));

        _overallBudget = overallBudget ?? DefaultBudget;
        _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
        _maximumBackoff = maximumBackoff ?? DefaultMaximumBackoff;

        if (_overallBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(overallBudget));
        if (_initialBackoff < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialBackoff));
        if (_maximumBackoff < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumBackoff));

        _maximumAttempts = maximumAttempts;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken,
        Action? beforeAttempt = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(requestFactory);

        var startedAt = _timeProvider.GetTimestamp();
        var firstRequest = CreateRequest(requestFactory);

        if (!IsGet(firstRequest))
            return await SendNonGetAsync(httpClient, firstRequest, completionOption, cancellationToken, beforeAttempt)
                .ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfBudgetExpired(startedAt);
        }
        catch
        {
            firstRequest.Dispose();
            throw;
        }

        var expectedUriText = GetUriText(firstRequest);
        var usedRequests = new HashSet<HttpRequestMessage>(ReferenceEqualityComparer.Instance);
        using var deadline = new CancellationTokenSource(GetRemainingBudget(startedAt), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        HttpRequestMessage? nextRequest = firstRequest;
        HttpResponseMessage? retryResponse = null;

        try
        {
            for (var attempt = 1; attempt <= _maximumAttempts; attempt++)
            {
                var request = nextRequest ?? CreateRequest(requestFactory);
                nextRequest = null;

                try
                {
                    ValidateGetAttempt(request, expectedUriText, usedRequests);
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfBudgetExpired(startedAt);
                    beforeAttempt?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfBudgetExpired(startedAt);
                }
                catch
                {
                    request.Dispose();
                    throw;
                }

                retryResponse?.Dispose();
                retryResponse = null;

                try
                {
                    var response = await SendGetAttemptAsync(
                            httpClient,
                            request,
                            completionOption,
                            cancellationToken,
                            deadline.Token,
                            linked.Token)
                        .ConfigureAwait(false);

                    if (!IsRetryable(response.StatusCode) || attempt == _maximumAttempts)
                        return response;

                    var retryAfter = GetRetryAfter(response);
                    var delay = Max(GetBackoff(attempt), retryAfter.Delay);
                    var remaining = GetRemainingBudget(startedAt);

                    if (retryAfter.IsValid && delay >= remaining)
                        return response;

                    retryResponse = response;
                    await DelayBeforeRetryAsync(delay, remaining, cancellationToken, deadline.Token, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (HttpRequestException exception) when (IsRetryable(exception) && attempt < _maximumAttempts)
                {
                    var remaining = GetRemainingBudget(startedAt);
                    await DelayBeforeRetryAsync(
                            GetBackoff(attempt),
                            remaining,
                            cancellationToken,
                            deadline.Token,
                            linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    !deadline.IsCancellationRequested &&
                    attempt < _maximumAttempts)
                {
                    var remaining = GetRemainingBudget(startedAt);
                    await DelayBeforeRetryAsync(
                            GetBackoff(attempt),
                            remaining,
                            cancellationToken,
                            deadline.Token,
                            linked.Token)
                        .ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("The retry policy completed without a response.");
        }
        finally
        {
            nextRequest?.Dispose();
            retryResponse?.Dispose();
        }
    }

    private static HttpRequestMessage CreateRequest(Func<HttpRequestMessage> requestFactory) =>
        requestFactory() ?? throw new InvalidOperationException("The request factory returned null.");

    private static bool IsGet(HttpRequestMessage request) =>
        string.Equals(request.Method.Method, HttpMethod.Get.Method, StringComparison.Ordinal);

    private static string? GetUriText(HttpRequestMessage request) => request.RequestUri?.OriginalString;

    private static void ValidateGetAttempt(
        HttpRequestMessage request,
        string? expectedUriText,
        HashSet<HttpRequestMessage> usedRequests)
    {
        if (!usedRequests.Add(request))
            throw new InvalidOperationException("The request factory must return a fresh request for every attempt.");
        if (!IsGet(request))
            throw new InvalidOperationException("A retried request must remain a GET request.");
        if (!string.Equals(GetUriText(request), expectedUriText, StringComparison.Ordinal))
            throw new InvalidOperationException("A retried request must retain the exact original request URI text.");
    }

    private static async Task<HttpResponseMessage> SendNonGetAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken,
        Action? beforeAttempt)
    {
        try
        {
            beforeAttempt?.Invoke();
            return await httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> SendGetAttemptAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken callerToken,
        CancellationToken deadlineToken,
        CancellationToken linkedToken)
    {
        Task<HttpResponseMessage> sendTask;
        try
        {
            sendTask = httpClient.SendAsync(request, completionOption, linkedToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        try
        {
            var response = await sendTask.WaitAsync(linkedToken).ConfigureAwait(false);
            request.Dispose();

            if (callerToken.IsCancellationRequested)
            {
                response.Dispose();
                throw new OperationCanceledException(callerToken);
            }
            if (deadlineToken.IsCancellationRequested)
            {
                response.Dispose();
                throw new TimeoutException("The HTTP read retry budget expired.");
            }

            return response;
        }
        catch (OperationCanceledException exception) when (linkedToken.IsCancellationRequested)
        {
            _ = ObserveLateSendAsync(sendTask, request);

            if (callerToken.IsCancellationRequested)
                throw new OperationCanceledException(exception.Message, exception, callerToken);
            if (deadlineToken.IsCancellationRequested)
                throw new TimeoutException("The HTTP read retry budget expired.", exception);

            throw;
        }
        catch (Exception exception) when (linkedToken.IsCancellationRequested)
        {
            _ = ObserveLateSendAsync(sendTask, request);

            if (callerToken.IsCancellationRequested)
                throw new OperationCanceledException(exception.Message, exception, callerToken);
            if (deadlineToken.IsCancellationRequested)
                throw new TimeoutException("The HTTP read retry budget expired.", exception);

            throw;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static async Task ObserveLateSendAsync(
        Task<HttpResponseMessage> sendTask,
        HttpRequestMessage request)
    {
        try
        {
            var response = await sendTask.ConfigureAwait(false);
            response.Dispose();
        }
        catch
        {
            // Deliberately observe late faults after the caller or budget has already completed.
        }
        finally
        {
            request.Dispose();
        }
    }

    private async Task DelayBeforeRetryAsync(
        TimeSpan delay,
        TimeSpan remaining,
        CancellationToken callerToken,
        CancellationToken deadlineToken,
        CancellationToken linkedToken)
    {
        callerToken.ThrowIfCancellationRequested();
        if (deadlineToken.IsCancellationRequested)
            throw new TimeoutException("The HTTP read retry budget expired.");
        if (delay >= remaining)
            throw new TimeoutException("The HTTP read retry budget expired.");

        try
        {
            await Task.Delay(delay, _timeProvider, linkedToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            if (callerToken.IsCancellationRequested)
                throw new OperationCanceledException(exception.Message, exception, callerToken);
            if (deadlineToken.IsCancellationRequested)
                throw new TimeoutException("The HTTP read retry budget expired.", exception);
            throw;
        }
    }

    private void ThrowIfBudgetExpired(long startedAt)
    {
        if (GetRemainingBudget(startedAt) <= TimeSpan.Zero)
            throw new TimeoutException("The HTTP read retry budget expired.");
    }

    private TimeSpan GetRemainingBudget(long startedAt) =>
        _overallBudget - _timeProvider.GetElapsedTime(startedAt);

    private TimeSpan GetBackoff(int completedAttempt)
    {
        var ticks = _initialBackoff.Ticks;
        for (var power = 1; power < completedAttempt && ticks < _maximumBackoff.Ticks; power++)
            ticks = Math.Min(_maximumBackoff.Ticks, ticks > long.MaxValue / 2 ? long.MaxValue : ticks * 2);

        return TimeSpan.FromTicks(Math.Min(ticks, _maximumBackoff.Ticks));
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private (bool IsValid, TimeSpan Delay) GetRetryAfter(HttpResponseMessage response)
    {
        try
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
                return (true, delta);
            if (retryAfter?.Date is { } date)
                return (true, Max(TimeSpan.Zero, date - _timeProvider.GetUtcNow()));
        }
        catch (FormatException)
        {
            // Invalid Retry-After values fall back to the policy backoff.
        }

        return (false, TimeSpan.Zero);
    }

    private static bool IsRetryable(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        (HttpStatusCode)429 or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private static bool IsRetryable(HttpRequestException exception) => exception.HttpRequestError is
        HttpRequestError.ConnectionError or
        HttpRequestError.NameResolutionError or
        HttpRequestError.ResponseEnded or
        HttpRequestError.Unknown;
}
