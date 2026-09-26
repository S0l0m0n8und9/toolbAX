# Log identifier redaction (H14)

## Status

Locally implemented and validated across App-owned `Trace` emission paths plus Core authentication/vault diagnostics. Saved session traces are identity-free, category-based diagnostics while existing UI errors, response bodies, and operation receipts retain their useful detail. This does not claim to sanitize arbitrary third-party `Trace` events.

## Verified baseline

[`RequestTrace`](../../../avalonia/toolBax.App/Services/RequestTrace.cs) persisted `ReasonPhrase` plus API/method/Endpoint. Endpoint removed origin/query but retained OData business keys such as `CustomersV3(dataAreaId='USMF',CustomerAccount='C000123')`; `Clean` only removed control characters.

[`DualWriteOpsViewModel`](../../../avalonia/toolBax.App/ViewModels/DualWriteOpsViewModel.cs) `Log` Warn/Err defaulted to `traceText ?? text`; its comments named persisted gateway hosts, map names, connection/request IDs, and status. `Traceable` handled body-bearing gateway exception types, while unknown exceptions fell back to message-based concise formatting. [`ProfilesViewModel`](../../../avalonia/toolBax.App/ViewModels/ProfilesViewModel.cs) logged environment name plus session-eviction exception. App last-resort handlers traced full exception text. [`SessionTraceLog`](../../../avalonia/toolBax.App/Services/SessionTraceLog.cs) persists these events.

## Desired invariants

Disk diagnostics omit record keys, business/profile/environment identifiers, gateway hosts, secret references, request paths/query/fragments/body, untrusted reason phrases, and exception messages, stack traces, or `ToString()` output. Detailed UI errors, response bodies, and operation receipts remain useful and unchanged.

Retained trace signals are finite allowlisted API/operation categories, known HTTP verbs, numeric status codes, exception types, and explicit failure/cancellation categories. Unknown input maps to a static fallback. Prefer omission and static categories to regex parsing, hashing, tokenization, or a generic logging framework.

## Implementation ownership

Owned production paths are `RequestTrace`; `SessionTraceLog`'s own failure diagnostics; the WebView2 dual-write sign-in failure trace; trace emission in `ProfilesViewModel`; persisted `Log`/`Traceable` diagnostics in `DualWriteOpsViewModel`; last-resort, degraded, and startup traces in `App.axaml.cs`; the `CompositionPreference` trace failure; and Core `VaultSecretReader` traces. The former `CoreProfileStore` warning belonged to an obsolete nontransactional cleanup helper removed by H09 and is deliberately not resurrected. Core `MetadataRetentionCoordinator`'s type-only warning and `CoreDualWriteConnector`'s expiry-only renewal event are already safe and remain unchanged.

No client behavior, transport/receipt/UI diagnostics, profile persistence, authentication trust policy, retries, paging, secret encryption or permission handling, or write flags change. `SessionTraceLog` retention, header, and listener behavior remain intact.

## Pre-mortem

1. Encoded keys bypass path parsing: omit request targets entirely and map API/verb inputs through finite allowlists with safe fallbacks.
2. Unknown exceptions echo identifiers: persist exception type and static failure category only, never message, `ToString()`, inner text, or stack data.
3. UI and disk diagnostics become conflated: keep existing detailed UI/receipt values and provide a separate typed/static persisted diagnostic.
4. A bypass call site still writes dynamic text: inventory every owned trace emission after the edit and cover each family with captured-trace canaries.
5. Useful diagnostic signals disappear: positively assert operation/API category, known verb, numeric status, exception type, and failure/cancellation signal remain.

## Acceptance

Meaningful RED/GREEN tests inject distinct canaries into raw and encoded request paths/keys, query, fragment, reason phrase, response body, profile/environment names, secret references, nested/gateway exceptions, and CRLF content. Captured `Trace` output must omit every canary while preserving finite categories, known verbs, numeric status, exception type, and failure/cancellation signals. Existing UI detail and operation receipts remain unchanged. Existing session-log retention/header/listener tests continue to pass. Synthetic handlers, temporary files/stores, and fake services only; no real browser launch, user profile database, authentication, or live backend calls.

CI=true Release builds pass for both the App and Core solutions with zero warnings/errors. Focused App coverage passes 268/268 across trace policy, operations/receipts, session-log retention, last-resort handling, profiles, profile storage, composition and startup; affected Core vault coverage passes 7/7. Evidence is under `artifacts/h14/`. Validation made no live calls or browser launch.

Parent review accepted the production implementation and then ran the complete suites. Core passed 456/456. The first complete App run passed 1,372/1,377; all five failures were pre-H14 diagnostic-contract assertions that still required endpoint paths or exact category casing. No production change was made in response. The three affected test files now assert the approved finite API/verb/numeric-status contract, complete target/key/host/query/body/header/token omission, retained returned-response/UI detail, and semantic category matching. The affected Core OData/Dataverse and write-outcome classes pass 87/87, the expanded H14 regression set passes 355/355, and the complete App suite passes 1,377/1,377. Review evidence is `artifacts/h14/review-focused-app.{log,trx}` and `artifacts/h14/review-full-app.{log,trx}`; the original five-failure run remains in `parent-full-app-test.log` and `parent-full-app.trx`.

### H09/H05/H10a integration checkpoint

H09 PR232 head `994a36418cc7fbe3a66e26f0faa5e716b6c2be76`, including current main/H05/H10a, was merged locally into H14. Two source conflicts were resolved. `CoreProfileStore` takes H09's removal of the obsolete cleanup helper instead of reviving its nontransactional path merely to retain a trace. `WebView2DualWriteSignIn` keeps H05's `_lifetime.TryPublish` guard and places H14's finite initialization-failure category/exception-type trace inside that guard, so a closed/late host publishes neither UI state nor diagnostic detail. `ProfilesViewModel` and `DualWriteOpsViewModel` auto-merged with H09 async persistence, leases, cancellation and publication behavior intact; their disk traces remain category/status/type only while UI/receipts stay detailed.

Both `CI=true` Release Core and App builds with WebView2 enabled pass with zero warnings/errors. Focused Core logging/profile/auth compatibility passes 131/131 and focused App logging/gateway/profile compatibility passes 485/485. Evidence is under `artifacts/h14/integrated-h09/`. This checkpoint used synthetic/local stores and fake transports only; no full suite, live platform call, authentication, browser, profile-clear, push or PR action was performed.
