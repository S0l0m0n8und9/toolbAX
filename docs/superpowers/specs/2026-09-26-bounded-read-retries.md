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

## Integration premises verified after H10a/H10c and main merge

- `HttpODataClient` performs one GET per already cycle-checked page target, parses and yields one page only after that request succeeds, and currently uses `ResponseHeadersRead`. Integration changes that send to `ResponseContentRead`, so each page body is buffered inside the policy's 30-second attempt budget; JSON parsing and the existing origin/cycle/publication guards remain outside it.
- `ODataMetadataProvider` performs one conditional metadata GET after its cache decision. Its fresh request factory must reproduce the captured absolute target and `If-None-Match` value on every attempt. XML parse and cache touch/save happen once, after the policy returns the final response.
- `CatalogService` has one shared metadata-XML fetch path and enrichment GETs. Metadata requests can carry `CatalogRequestContext.MetadataCachePartition`; factories must bind the captured partition every time and reproduce a captured conditional ETag. Parse/enrichment/cache publication remains outside the retry policy and keeps H10a cancellation guards.
- `DualWriteGatewayClient` already separates GET reads from `SendMutationAsync`. Only the GET branch enters the retry policy. Mutation evidence, cancellation classification and the auth handler's independent single 401 renewal/replay remain unchanged; the retry policy never retries 401.
- App `CoreODataClient` and `CoreDataverseClient` acquire authentication once for an immutable environment snapshot, validate absolute/origin targets before dispatch, and already expose a full `EnvironmentIdentity` current-context check. GET retry factories must reproduce the captured target and snapshotted headers/token, while `beforeAttempt` rechecks that full identity before every send. Mutation methods remain on their existing one-dispatch path.
- `CoreConnectionTester` deliberately probes the supplied immutable displayed draft, which may differ from the active environment. It force-refreshes authentication once, then retries the same captured absolute target/token/headers. Profiles owns draft/selection invalidation by canceling the probe; `beforeAttempt` therefore rechecks the caller token rather than an active-environment accessor. No App composition change is required.
- All integrated sends use `ResponseContentRead`. This bounds transport plus body buffering for one page/response, not JSON/XML parsing, cache work, a whole iterator or a whole UI action. No caller may wrap a second retry around these boundaries.

## Integration pre-mortem

1. **A fresh attempt loses conditional headers or request context.** An ETag or `CatalogRequestContext.MetadataCachePartition` present only on attempt one can change the cache partition or duplicate a full download. Mitigation: capture immutable values once and rebuild the exact headers/options in every fresh request factory; assert every attempt.
2. **An environment switch or canceled draft probe during backoff sends an obsolete request.** Mitigation: App OData/Dataverse capture one absolute target and token, then use `beforeAttempt` to compare the full captured `EnvironmentIdentity` before every dispatch. Connection probes remain pinned to their immutable displayed draft and recheck caller cancellation before every attempt. Either invalidation stops the second send.
3. **A body stalls after headers and escapes the retry budget.** Mitigation: use `ResponseContentRead` for every integrated GET so transport and body buffering complete within the policy attempt/budget. Parse work remains separately cancellation-aware and nonretryable.
4. **A mutation or authentication POST is accidentally replayed.** Mitigation: integrate only explicit GET call sites. Keep auth acquisition/token refresh outside the policy, retain `SendMutationAsync`, and preserve existing one-logical-dispatch POST/PATCH/PUT/DELETE tests. The auth pipeline may separately renew once after a gateway 401; the policy never retries 401.
5. **A retry success publishes a page or cache entry twice.** Mitigation: retry only the single HTTP exchange and expose only the final response. Parse, page yield, cache touch/save and UI publication happen once after success; existing H10c cycle and H10a publication guards stay in force.

## Integrated implementation and focused validation

The policy now wraps the single GET exchange in `HttpODataClient`, `ODataMetadataProvider`, all three `CatalogService` metadata/enrichment request sites, the GET-only branch of `DualWriteGatewayClient`, App `CoreODataClient` GETs, App `CoreDataverseClient`, and immutable-draft `CoreConnectionTester` probes. Constructors accept an optional policy for deterministic tests and otherwise use the foundation defaults. Every integration uses a fresh request factory and `ResponseContentRead`; parse, cache, page yield and UI publication remain after the final response.

The gateway's generic router retains its original explicit non-GET return to `SendMutationAsync`, which remains unchanged. A separately named `SendReadAsync` owns GET retry construction and retains the prior optional GET body behavior. Public gateway mutation regression sends one POST with its full JSON payload on 429. App OData public POST/PATCH/PUT/DELETE controls each dispatch once on 429/503 and preserve their request bodies/H03 evidence behavior. Authentication acquisition and gateway 401 renewal remain outside the policy.

An actual `HttpODataClient` 503→success test was behaviorally RED before integration: the first 503 escaped and no page was yielded. Integrated actual-client coverage now passes Core 8/8 and App 18/18 for transient success/exhaustion, one-page yield, a real warm-cache ETag retained with Catalog partition across 503→304 retry, fixed headers/tokens/targets, both OData and Dataverse environment drift, probe cancellation during backoff, a valid too-long `Retry-After` returned as final 429 without waiting, buffered-body budget with late disposal, non-GET buffering semantics, gateway/App mutation isolation and auth-once behavior. An executable negative control proves direct `ResponseHeadersRead` serializes later with caller token `None`, while the actual `OPTIONS` path buffers with HttpClient's cancellable deadline token. `HEAD` has framework-defined no-body semantics and is separately controlled as a single dispatch without a false buffering claim. The body-budget test uses a gated 500 ms budget and asserts bounded completion under build load. Two existing timeout controls were updated from one call to the intentional bounded three-attempt exhaustion while preserving their failure-result semantics; auth timeout remains one non-HTTP failure.

The reviewed integration checkpoint is `1c540fe`. H10a head `df63c09b1d60f43feed3366820dd7afbae143830`, including merged H05 source `573a6a42dc0d2f818ef4c5b3c904713cd9405ec4`, was integrated as merge `ecf0fc2`; the only conflict was the hardening tracker, resolved by retaining delivered H05/integrated H10a plus the complete H10b/H10c histories. Production sources merged without conflict.

Final integrated `CI=true` Release builds pass for both Core and App (`EnableWebView2=true`) solutions with zero warnings/errors. The complete Core suite passes 655/655 and the complete App suite passes 1,637/1,637. Earlier focused compatibility remains Core 151/151 and App 329/329. Evidence is under `artifacts/h10b/{integration,integrated-h05}/`, including `integrated-h05/full-{core,app}-{build,test}.log` and `.trx`. Validation used fake transports/local stores only; no live service, authentication, browser, sign-in, profile-clear, push or PR action occurred. H10b publication waits for the dependent H10c paging work to reach `main`.

## Published H09 review integration

Published H09 review head `b1caddb15f29721b1653d3241c59405616712558` was merged into the clean H10b documentation checkpoint `2e04b8400595a22acd24f2efedfdcf571ef5c645` as `7a1f9720a339b5b2cbb175ac77341b0fbed620c5`. The only conflict was this hardening tracker; it was resolved mechanically by retaining H09/H10a from the published head and H10b/H10c from this branch. No production source conflict or retry-policy redesign occurred.

Sequential `CI=true` Release builds passed with zero warnings/errors. The complete App suite with WebView2 enabled passed 1,686/1,686. The first normal Core run and a bounded 120-second diagnostic rerun both aborted at `SendAsync_AppliesExponentialBackoffUsingVirtualTime`; the latter had passed 512 tests before the inactivity timeout. The isolated test passed, identifying a test-scheduler race rather than a production failure: observing the first handler call did not prove that the continuation had registered its virtual backoff timer before the test advanced the clock. Those aborts and hang dumps remain under `artifacts/h10b/integrated-h09-review/{hang-diagnostic,rerun}/`.

The test-owned `ManualTimeProvider` now exposes its current scheduled-timer count. Tests that advance through or cancel a retry delay wait until both the existing total-budget timer and the intended backoff timer are pending. Ignoring-handler budget/cancellation tests remain unchanged because they do not schedule a backoff. No production source changed. Focused retry-policy tests pass 47/47, and the unchanged default parallel Core gate then passed 671/671 in 19 seconds. Final proof is under `artifacts/h10b/integrated-h09-review/final/`.

This branch remains unpublished. H10b still waits for H10c and the final H09 correction to reach `main`; no newer H09 change was folded into this checkpoint.
