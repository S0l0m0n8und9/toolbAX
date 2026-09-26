# Bounded read retries (H10b foundation)

## Scope

Small typed Core policy only; no client integration, DI, Polly, UI, settings, logging, paging, or mutation changes. GET only; all other methods send once unchanged.

`ReadRetryPolicy.SendAsync` takes an `HttpClient`, a fresh `HttpRequestMessage` factory, the caller's `HttpCompletionOption` and cancellation token, and an optional synchronous pre-attempt action. Defaults are three attempts total, a 30-second total budget, and 250ms/500ms exponential backoff capped at two seconds. An injectable `TimeProvider` supplies elapsed, deadline, and delay semantics without a new package.

Retryable HTTP statuses are 408, 429, 500, 502, 503, and 504. Retryable `HttpRequestException` categories are connection, name-resolution, response-ended, and legacy unknown errors. TLS, authentication, configuration, and protocol failures are excluded. A timeout-shaped `OperationCanceledException` may retry only while both caller and total-budget tokens remain live. Caller cancellation remains `OperationCanceledException`; total-budget expiry is `TimeoutException`.

`Retry-After` delta and date forms are minimum waits combined with policy backoff. When a valid server wait cannot fit the remaining budget, the last response is returned; the server wait is never shortened. Invalid values fall back to policy backoff.

Every GET attempt uses a fresh factory result and retains the exact `GET` method and request URI text from the first request. Drift and reused request instances are rejected before dispatch. The pre-attempt action runs before every send so a future client can re-check environment scope. Factory and pre-attempt failures propagate and are not retried. Non-GET requests retain the caller's existing one-send behavior and never gain a policy timeout or replay.

The policy owns factory-created requests and intermediate responses; the caller owns the returned final response. GET attempts and delays use linked caller/deadline cancellation. A handler that ignores cancellation cannot hold the caller open: its late task is observed, its request remains alive until send completion, and any late response is disposed. No request URI, credential, header, or body is logged.

`HttpCompletionOption` is forwarded unchanged. With `ResponseHeadersRead`, body read and parse work happens after this policy's attempt has completed and is therefore outside the retry budget and retry protection until client integration explicitly defines that boundary.

## Pre-mortem

1. Write replay: inspect the actual first request and retry exact uppercase `GET` only; every other verb is sent once.
2. Excessive or service-throttle retries: cap attempts and total elapsed time, and honor valid `Retry-After` as a minimum instead of shortening it to local backoff.
3. Wrong request or scope retry: require a fresh request with unchanged method and exact URI text, then run the caller's scope guard before every dispatch.
4. Ignored cancellation leaking tasks or responses: race the send against linked cancellation, retain the active request until the handler completes, observe late faults, and dispose late responses.
5. Timeout and caller-cancellation misclassification: give caller cancellation precedence as `OperationCanceledException`; report only total-budget expiry as `TimeoutException`; retry timeout-shaped cancellation only while both tokens remain live.

## Acceptance

Focused Core tests use virtual time and gated handlers only. They cover retryable and permanent statuses, selected and excluded transport failures, timeout-shaped cancellation, success and exhaustion, exponential backoff, delta/date/too-long/invalid `Retry-After`, total budget including factory/guard time, caller cancellation before send/during send/during delay, ignored-send late response/fault cleanup and completion races, fresh request and exact target checks, pre-attempt refusal, factory failure, forwarded completion mode, and all supported non-GET verbs as exactly one send. No live calls.

This is only the H10b policy foundation. H10b remains incomplete until the intended read clients adopt the policy with their environment guard and client-specific response/body boundary.
