# Uncertain write outcomes (H03)

## Status and scope

Accepted by the parent for phased implementation. Phase 1 changes transport evidence only; parent reviews it before any UI work. No package additions, auth/paging refactor, other hardening work, live calls or PR until the entire H03 scope is complete.

## Accepted behavior

Cancellation or lost transport after submission is not proof of rollback. Keep immutable in-memory last-write receipts with captured environment identity/name/URL, operation/target/time, observed HTTP status and locator or gateway request ID, and per-project debug progress. No persistent journal, payload/token/header logging, automatic resend or invented idempotency keys. Known preflight/decline/auth refusal may be not sent; absent dispatch evidence after invoking a write is conservatively unknown. HTTP acknowledgment, including 2xx, is distinct from terminal asynchronous completion. A 5xx or transport loss does not prove the write was unapplied.

Later UI reconciliation is a captured-scope GET: PATCH/DELETE original resource; POST only observed same-origin Location/OData-EntityId, otherwise manual Query Builder guidance; lifecycle status by known request ID or map refresh; debug configuration GET for captured projects. Readback reports current state, not causation. Environment/session drift disables or discards reconciliation. Never automatically replay a write after an uncertain outcome; a new user-confirmed write remains a separate attempt. Debug legs remain separately pending, acknowledged, unknown or not attempted.

## Phase 1 transport contract

Keep constructors and signatures source-compatible through optional/init metadata. ODataResponse gains nullable dispatch-start evidence (null means unprovided), body completeness and safe read-error evidence while retaining observed status/headers. IsSuccess requires complete body evidence. Typed mutation cancellation remains OperationCanceledException-compatible and carries dispatch evidence plus any observed response. Local validation/no environment/origin/auth refusal is not dispatched; crossing HttpClient dispatch only means may have been sent, never confirmed delivery. Injected handler/auth pipelines have an opaque boundary, so failures inside them remain conservative.

Gateway mutation failures and cancellation expose the same small evidence contract; successful action responses retain HTTP acknowledgment and request ID. Existing complete-body HTTP exception messages and redaction behavior remain compatible. New diagnostic summaries contain no payload/header values. Mutations can use ResponseHeadersRead, but a linked deadline must preserve HttpClient.Timeout across the entire exchange including gateway auth-handler work and body consumption; InfiniteTimeSpan remains unlimited. Read calls keep their existing cancellation/result behavior. Explicit 401 auth-refresh behavior remains unchanged; no new retry after ambiguous failure.

## Five pre-mortems

1. Cancellation after server commit looks unsent: retain dispatch/HTTP evidence and later show the immutable receipt.
2. A body failure loses a 2xx or locator: capture status and headers before consuming the body.
3. Headers-only completion drops the body timeout: preserve the configured total exchange deadline and test a gated body.
4. Reconciliation targets edited selection/environment: capture immutable scope and recheck full identity before and after reads.
5. Partial batch writes look all-or-none: retain per-target progress and never automatically retry.

## Validation

Phase 1 uses gated local handlers/content, no sleeps or live services. Behavioral RED/GREEN covers pre-dispatch cancellation/refusal, simulated server-state change followed by connection loss/cancellation, 2xx headers followed by failing/cancelled body, full body deadline and InfiniteTimeSpan, success/header retention and unchanged GET cancellation. Preserve gateway complete-body exception diagnostics and existing auth-refresh tests. Save completed logs/TRX under ignored artifacts/h03. Parent owns full gates and UI design review after transport source-ready.

Headers-first mutation reads also explicitly retain HttpClient.MaxResponseContentBufferSize by buffering with the configured limit and the total-exchange cancellation token before reading the string. Per [.NET 10 HttpCompletionOption documentation](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0), ResponseHeadersRead stops both timeout and automatic buffer-limit enforcement at the headers. A limit failure must retain observed headers/status as incomplete evidence.
## Phase 1 transport checkpoint

Implemented transport-only evidence; UI receipts/reconciliation remain pending parent review and a later phase. Existing constructors/positional DTO parameters remain compatible.

- `ODataResponse`: nullable `DispatchStarted` (null = unprovided), `BodyComplete` (default true for existing providers), safe type-only `BodyReadError`. `IsSuccess` requires 2xx and complete body. The real mutation client supplies false for observed preflight/auth/identity refusal and true only on entering HttpClient; true is not delivery evidence. Observed HTTP status/headers survive incomplete body reads.
- `ODataWriteCanceledException` remains OperationCanceledException-compatible and carries `DispatchStarted` plus optional `ObservedResponse`, including timeout/body-cancellation evidence.
- `DualWriteMutationEvidence`: nullable dispatch marker, optional HTTP status/headers, body completeness and safe type-only failure category. `IDualWriteMutationFailure` exposes it on typed transport/cancellation/argument-validation errors and optionally on the source-compatible `DualWriteGatewayException`. Complete HTTP failures keep their existing body-bearing diagnostic message; opaque handler failures cannot manufacture an observed HTTP status. `DualWriteActionResponse.Acknowledgment` retains HTTP evidence separately from its request ID/state and never implies terminal completion.
- Mutation buffering explicitly enforces the existing HttpClient size limit and one linked total deadline over dispatch/auth-handler/body work. InfiniteTimeSpan is honored. GET paths and the explicit 401 refresh/replay behavior are preserved. No new logging, journal, retry or UI code was added.

Completed behavioral RED: initial OData 10/10 failed; gateway 7/8 failed with unchanged GET cancellation passing. Removing only configured-buffer enforcement reproduced one failure in each client. An opaque-handler HTTP exception reproduced one missing-dispatch-evidence failure. Final CI=true Release focused GREEN: OData 41/41 and gateway/related/auth-refresh 39/39, zero failed/skipped. Saved logs and TRX are under `artifacts/h03/`: `odata-red`, `gateway-red`, `odata-buffer-red`, `gateway-buffer-red`, `gateway-opaque-red`, `odata-final-green`, `gateway-final-green`. No full-suite or live-service claim. Opaque HttpClient pipelines remain conservatively may-have-been-sent; transport acknowledgment does not establish rollback, delivery or terminal completion.
