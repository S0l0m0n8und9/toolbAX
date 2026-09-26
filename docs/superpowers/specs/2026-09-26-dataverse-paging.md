# Dataverse paging cycles (H10c App-reader phase)

**Status:** Implemented and fully validated after final H09 cache-contract integration; pending parent review and hosted delivery.

Use Core `PageVisitTracker` once per traversal, before each initial/continuation dispatch. Map component/map/solution traversals clear their sink and return a static incomplete-paging error on repeat. Relative continuation identity must match the captured Dataverse API-base-plus-trimmed-path dispatch behavior; absolute links remain verbatim and existing origin checks remain authoritative.

Virtual tables gain `ParsePage` returning records plus continuation while `Parse` remains compatible. Reader collection is all-or-fail: later HTTP, malformed page, cycle, cancellation, or environment drift returns failure with no partial table list. Capture identity/API base once; follow relative and absolute pages under existing client guard.

## Pre-mortem

1. Duplicate continuation loops forever: visit tracker before dispatch.
2. Partial virtual inventory looks complete: all-or-fail result.
3. Later-page failure leaks rows: do not publish accumulator.
4. Relative key differs from dispatch: pin API-base resolution.
5. Drift/cancellation starts another page: checks before/after awaits.

## Implemented result

`CoreDualWriteMapReader.PageAllAsync` now resolves the initial request and every relative continuation against the one captured API base, records each actual request target before dispatch, clears its accumulator on cycle, drift, parsing failure or later HTTP failure, and returns the existing empty failure result. Absolute continuations remain unchanged for `CoreDataverseClient` to apply its origin guard.

`VirtualTableMetadataParser.ParsePage` returns validated tables plus the continuation and `Parse` delegates to it for compatibility. `CoreVirtualTableReader` accepts the optional active-environment accessor used by existing fakes, while the real `App.axaml.cs` factory now passes its existing accessor. The reader pins full environment identity/API base at entry, follows empty or physical-only pages with continuations, checks drift and cancellation around awaits, and publishes only a fully completed inventory.

The final `CI=true` Release App gate includes the map reader, virtual reader/parser, cancellation, Query and render compatibility suites within the 367/367 result recorded at `artifacts/h10c/completion/app-focused.{log,trx}`.

Parent-review Query corrections did not change the Dataverse reader implementation. The forced post-retrospective rebuild and expanded compatibility gate passed 373/373, with the unchanged Core regression at 29/29. See the parent-review evidence section in `2026-09-26-paging-integrity.md`.

## Integrated validation

H09/H10a and the PR232 test synchronization correction merged without Dataverse-reader conflicts. Map, component, solution and virtual-table traversals still capture one environment identity/API base, reject repeated or malformed pages before partial publication, and retain H10a cancellation checks around every dispatch/follow-on. The integrated `CI=true` Release Core build passed with zero warnings/errors and 616/616 tests; the App WebView2 build passed with zero warnings/errors and 1,660/1,660 tests. Evidence is `artifacts/h10c/integrated-h09/`. No live Dataverse call was made.

Combined PR232 review head `b1caddb15f29721b1653d3241c59405616712558` then merged without Dataverse-reader changes. Core remains unchanged at 616/616; the rebuilt App WebView2 solution had zero warnings/errors and passed 1,668/1,668. Evidence is `artifacts/h10c/integrated-h09-review/`. No live Dataverse call was made.

Final PR232 head `1890a871c864532b7c2787580eb51e3d6f04f30d` merged cleanly as `7df7f26e4aca9e48cefdfa9301e422c6bff003dd` with no Dataverse-reader conflict or source change. Parent verified Core source/tests unchanged from the 616/616 gate. The rebuilt `CI=true` Release App WebView2 solution had zero warnings/errors and passed 1,670/1,670 with zero failures/skips. Evidence is `artifacts/h10c/integrated-h09-cache-contract/`. No live Dataverse call was made; hosted delivery still waits for PR #232 merge and current-main ancestry integration.

## PR #233 bare-host review correction

Greptile P1 finding `4110256886` (thread `PRRT_kwDOQmkX-M6mN41f`) identified a cross-layer mismatch: `CoreVirtualTableReader` correctly pinned an initial absolute request from `ResourceUrlNormalizer`, but `CoreDataverseClient` compared that request against the raw profile text. `RequestOriginGuard`'s legacy `StartsWith("http")` scheme heuristic therefore rejected valid scheme-less hosts such as `http-preview.crm.dynamics.com` before dispatch.

The client now captures `NormalizeDataverseResourceBaseUrl` once per request and uses that same canonical resource base for relative URI construction and absolute-origin comparison. Absolute pinned links remain verbatim. Scheme, host and port equality remains fail-closed before token acquisition, and the full environment/cancellation checks still guard dispatch and publication. This does not rewrite `RequestOriginGuard` or close D03 for its other untagged callers.

The behavioral RED composed the real client and virtual-table reader: all four bare `http`/`https`-prefixed, casing, whitespace and API-suffix profile variants failed while 20 controls passed. Foreign-host, HTTPS-to-HTTP downgrade and alternate-port requests were all refused with zero token acquisitions and zero HTTP dispatches. After the bounded fix, the same focused set passed 24/24, including correct HTTPS initial and continuation requests and a complete virtual-table inventory. The final `CI=true` Release App build with WebView2 passed with zero warnings/errors, and the full App suite passed 1,677/1,677 with zero failures/skips. Evidence is `artifacts/h10c/pr233-review/bare-host-{red,green}.{log,trx}`, `app-build.log`, `app-test-full.log`, and `app-test-full.trx`. Fake auth and HTTP handlers only; no live endpoint was used.
