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

## Accepted phase 2 UI decisions

Post and Ops keep immutable in-memory last-attempt receipts, separate from mutable editors/pickers and ordinary refresh status. Capture full environment identity plus display name/URL, exact method/action/targets, start time, dispatch/HTTP evidence and optional gateway request ID. Debug keeps per-project desired value and not-attempted/not-sent/acknowledged/unknown progress. A later readback never erases original uncertainty. New confirmations explicitly mention an unresolved prior attempt in the same scope.

Post confirmation is token-aware. One-owner, non-queuing send/readback admission owns its cancellation; rejected direct calls cannot replace the owner, token or receipt. Explicit GET readback targets the captured PATCH/DELETE resource, or a POST Location/OData-EntityId resolved against the captured endpoint and checked for same origin, no userinfo and no fragment. With no safe locator, show precise manual Query Builder guidance for the captured environment/target/time. Check full identity before and after; never auto-switch or replay.

Ops retains captured session identity/CID/action/maps/request ID. Stopped polling preserves submitted/acknowledged status and ID. Explicit reconciliation uses GetStatus for a known ID or GetMaps for the captured CID; debug only GETs the captured projects' configuration. Readback reports current state, not causation. Reuse the Ops operation lease, maintain confirmation/disposal/mutation barriers, and do not start follow-on requests after cancellation or drift. Receipts/warnings/readback use stacked measured panels and existing resources. New Trace warnings use static categories/status only; existing gateway body redaction stays intact.

Phase 2 tests use gated local fakes and attached headless views for uncertain writes, cancellation before confirmation, retained polling ID, partial debug progress, incomplete acknowledged responses, safe captured readback, rejected locators, drift/disposal/overlap and visible warning/readback separation. No sleeps or live calls. Parent review, current-main integration and full gates precede the H03 PR.

## Phase 2 source-ready checkpoint

Implemented `PostWriteReceipt`, `LifecycleWriteReceipt` and `DebugWriteReceipt` with immutable captured `WriteScope` and per-project observations. Their local AttemptId exists only to enforce receipt ownership and is never an HTTP idempotency key, transmitted value or persisted journal. The narrow `WriteOperationCommand` acquires the VM lease before allocating cancellation state and preserves Cancel, ExecutionTask and CanBeCanceled for the accepted owner; direct rejected calls cannot replace them. Confirmation uses the accepted cancellation token and names any prior unconfirmed attempt in the same environment scope.

Readback is explicit and read-only, with independent displayed text. PATCH/DELETE retain the complete captured F&O endpoint path prefix; POST uses only an observed same-origin locator without userinfo/fragment and otherwise gives captured manual Query Builder guidance. Ops reads its captured request ID or CID/maps; debug reads the captured projects and renders unsupported numeric flags as unknown. HTTP acknowledgment is distinct from terminal gateway completion, legacy action DTOs do not invent a numeric HTTP status, and local synthetic auth failures do not become observed HTTP evidence. A successful current-state GET never rewrites 5xx/incomplete/unknown original evidence.

Parent-approved drift behavior: only a still-owned historical receipt can settle from a late write response after full-identity drift, retaining the old scope and actual status/locator/request ID. The VM must be undisposed and the attempt ID still current. Ordinary bodies/grids, automatic polling/refresh and all follow-on requests remain blocked; the status names the context change. Readback results are discarded after drift; disposal blocks all late receipt settlement. Two stacked measured panels show original evidence and current-state readback separately.

Completed UI behavioral RED: four initial cancellation/unknown/acknowledgment/polling cases failed before implementation; the endpoint-prefix readback regression failed before its narrow correction. Numeric debug flags 2/-1 failed before the authorized 0/1-only parser fix. Final focused CI=true Release GREEN: Post/Ops/Shell/attached-render acceptance 255/255; Core debug parser 21/21, zero failed/skipped. Phase 1 transport evidence remains 41 OData and 39 gateway/related/auth-refresh tests. Artifacts: `ui-initial-red.{log,trx}`, `readback-prefix-red.{log,trx}`, `debug-numeric-red.{log,trx}`, `debug-numeric-green.{log,trx}`, `ui-source-ready-green.{log,trx}` under `artifacts/h03/`. Full suites, latest-main integration and PR/review/merge remain parent gates; no live calls or remote operations were performed.
