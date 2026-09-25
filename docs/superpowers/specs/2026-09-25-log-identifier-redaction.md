# Log identifier redaction (H14 proposal)

## Status

Proposal only; not accepted for implementation. No production, test, tracker, or remote changes are proposed.

## Verified baseline

[`RequestTrace`](../../avalonia/toolBax.App/Services/RequestTrace.cs) persists `ReasonPhrase` plus API/method/Endpoint. Endpoint removes origin/query but retains OData business keys such as `CustomersV3(dataAreaId='USMF',CustomerAccount='C000123')`; `Clean` only removes control characters.

[`DualWriteOpsViewModel`](../../avalonia/toolBax.App/ViewModels/DualWriteOpsViewModel.cs) `Log` Warn/Err defaults to `traceText ?? text`; its comments name persisted gateway hosts, map names, connection/request IDs, and status. `Traceable` handles body-bearing gateway exception types, while unknown exceptions fall back to message-based concise formatting. [`ProfilesViewModel`](../../avalonia/toolBax.App/ViewModels/ProfilesViewModel.cs) logs environment name plus session-eviction exception. App last-resort handlers trace full exception text. [`SessionTraceLog`](../../avalonia/toolBax.App/Services/SessionTraceLog.cs) persists these events.

## Desired invariants

Disk diagnostics omit record/business identifiers and untrusted request/server text; detailed UI errors remain useful. Retain safe operation category, known verb, numeric HTTP status, exception type, cancellation, and actual error signals. Prefer omission or static route categories to parsing arbitrary free-form request targets. Do not introduce hashing/tokenization unless required.

The parent must choose whether to tighten all App-owned trace emission/exception formatting coherently or limit the work to request tracing. Call-site changes cannot claim universal third-party log sanitization.

## Candidate ownership

Likely production candidates are `RequestTrace`, Ops trace helper, Profiles trace calls, and App exception reporting, with targeted tests. This proposal does not redesign them.

## Pre-mortem

1. Encoded/alternate keys escape regex: use conservative persisted fields.
2. Unknown exception echoes records: use exception-type allowlists.
3. UI becomes unhelpful when disk/UI text is conflated: separate UI and persisted text.
4. Legacy logger bypasses policy: inventory call sites.
5. Redaction removes every signal: positive safe-diagnostic tests.

## Acceptance proposal

Cover raw/encoded key paths, fragments, hostile reason phrases, gateway exceptions/status failures, profile names, and CRLF. Prove no sent-request/UI-detail change, safe diagnostics remain, and existing log retention remains. No live calls.
