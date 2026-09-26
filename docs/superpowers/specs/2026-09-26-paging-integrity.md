# Paging Integrity

**Status:** Implemented and locally validated across Core and App. Uncommitted pending parent review.

## Phase 1 boundary

Every `HttpODataClient.StreamAsync` traversal owns a case-sensitive set of actual request targets. Before dispatch, it resolves the next target exactly as the configured `HttpClient` will send it. Absolute HTTP targets use a canonical escaped request URL: scheme, host and default port are normalized by `System.Uri`; escaped path and query remain case-sensitive; fragments are omitted because they are not sent in an HTTP request. Relative targets resolve against `HttpClient.BaseAddress`. When no absolute target can be resolved, the raw ordinal text remains the visit key and existing invalid-relative behavior decides dispatch.

The initial request and every continuation are recorded before dispatch. A repeated target, including self-links and A-to-B-to-A chains, throws an `InvalidOperationException`-compatible paging error stating that results are incomplete. The message contains no link, query, row or identifier data. Earlier unique pages may already have streamed to the caller; the repeated page is never dispatched or yielded. Case-distinct path/query values remain distinct.

Server continuations retain the existing same-origin rule. Resolution used for visit identity must be the same resolution used for dispatch, and the resolved continuation must match the initial trusted origin before a request can leave the client. Paging detection does not add retries, page caps, query reordering, decoding or altered URI semantics. Caller cancellation keeps precedence over paging, auth-recovery and transport errors.

`CatalogService` uses the same key rule only for its optional Data Management target-map field-length enrichment. A repeated target or any later unsuccessful page makes that candidate's enrichment unavailable and discards every row collected for it. The outer candidate fallback and base metadata remain available, so incomplete field-length evidence is never presented as complete metadata. Caller cancellation still propagates.

## Failure pre-mortem

1. **A self-link or A-to-B-to-A chain causes endless calls or duplicate rows.** Record every actual target, including the initial request, before dispatch and fail before a repeat can leave the client.
2. **Case-insensitive comparison merges distinct skip tokens.** Use ordinal case-sensitive escaped path/query keys while allowing `System.Uri` to canonicalize only scheme, authority and default port.
3. **The visit key differs from the URI dispatched or weakens origin protection.** Resolve relative targets through the same `HttpClient.BaseAddress` rule, dispatch the resolved URI, and apply the existing same-origin guard to that exact continuation.
4. **A partial target-map enrichment becomes authoritative.** Treat repeats and later HTTP failures as unavailable enrichment and discard the candidate's accumulated rows before outer fallback.
5. **Cancellation is converted into paging, auth or transport failure.** Check the caller token before visit validation, dispatch, parsing, error translation and page publication; cancellation remains the observable result.

## Deterministic acceptance

- Initial self-links and A-to-B-to-A chains stop before repeat dispatch with an incomplete-results error.
- Relative/absolute aliases resolve through the actual base address to one visit identity; case-distinct skip tokens remain distinct.
- Valid multi-page streams still yield all pages; earlier unique pages remain observable when the next target repeats.
- Foreign-origin continuations dispatch zero requests to the foreign host.
- A cancellation-ignoring response still resolves to caller cancellation.
- Target-map repeat and later-page failure discard partial lengths while base metadata and candidate fallback remain usable.

Tests use bounded fake transports and temporary cache databases only. No live backend, portal or authentication calls are part of validation.

## Phase 1 implementation and local validation

Core uses the small shared `FoToolbox.Core.Net.PageVisitTracker` API: `TryVisit(string, Uri?, out Uri?)` records one actual target per traversal, and `ResolveRequestUri(string, Uri?)` exposes the exact HTTP(S) resolution used for dispatch and origin checks. `HttpODataClient` now rejects repeat targets before dispatch and treats present non-string, empty or whitespace continuations as incomplete. It validates a continuation against the resolved URI actually sent through `HttpClient.BaseAddress`. Catalog target-map enrichment uses the same visit identity and discards the candidate's accumulated rows on a repeat, foreign continuation or unsuccessful page.

The initial bounded behavioral RED ran 6 cases: 5 cycle/enrichment assertions failed and the case-sensitive control passed. Parent-review RED ran 8 cases: all 6 actual-origin and invalid-present-continuation assertions failed while absent/null final-page controls passed. Final focused cycle, OData, cancellation and catalog suites pass 72/72 under `CI=true` Release. Both Core target frameworks build with zero warnings/errors. Evidence is under `artifacts/h10c/core-phase1/` (`h10c-red`, `h10c-review-red`, `h10c-review-green`, `h10c-focused-green`, and `h10c-core-build`).

Parent reviewed the Core paging diff and the actual-origin/strict-continuation behavior. The complete Core solution then built under `CI=true` Release with zero warnings/errors and passed 481/481 tests with no failures or skips. Proof is `parent-full-core-{build,test}.log` and `parent-full-core.trx` under `artifacts/h10c/core-phase1/`. The accepted Core phase remains uncommitted because automatic approval review blocked the broader checkpoint. App parser/reader work was denied; a pre-stop `QueryBuilderViewModel.cs` partial edit (+62/-11) remains unvalidated with no new tests and is not part of this accepted Core evidence. H10c overall remains incomplete.

## Historical acceptance gap identified before resume

At the stopped checkpoint, `HttpODataClient.cs` treated HTTP 200 `{}` or `{"value":{}}` as empty rows and a missing nextLink as terminal. `CatalogService.cs` could likewise skip that malformed collection envelope and return earlier accumulated enrichment rows. The trigger was a healthy first page followed by either body without nextLink. Core blank/syntactically invalid JSON already threw and was not the same as Query's blank-body case. This was source-only confirmation at that point; the earlier Core 481/481 validation did not cover these cases.

## Completion after approval

The accepted traversal tracker now covers the remaining collection-envelope gap: a successful response must be an object with an array at `value`. Shared streaming throws a static incomplete-results error after any earlier unique pages; optional target-map enrichment discards the whole candidate and continues its existing fallback behavior. The App map, solution-component and solution walks use the same per-traversal request identity and captured Dataverse API base. Virtual-table parsing exposes `ParsePage` while retaining `Parse`, and the reader follows every page all-or-fail under a captured full environment identity. Query preview/load-more history records only guarded successful page commits, while export owns a separate traversal and never opens the save service after a cycle or malformed page.

Deterministic RED for the final Core gap was 0/2 passing: both `{}` and `{"value":{}}` after a good page completed silently before the fix. The corresponding paging class is now 14/14. Final sequential `CI=true` Release validation built Core and App with zero warnings/errors, then passed Core focused paging/transport/catalog tests 29/29 and App map/virtual/Query/render/H10a/H03 compatibility tests 367/367. Logs and TRX files are under `artifacts/h10c/completion/`. No live service, authentication, portal or tenant call was made.

## Parent-review correction and retrospective verification

Parent review removed an unsafe inference in the Query export catch: an `InvalidOperationException` containing the word `incomplete` can come from `SaveTextAsync` after a destination has been opened or partly written, so the outer catch again reports only `Export failed`. Cycle exits still make their pre-save no-file statement, and malformed-page tests prove zero save-service calls. Query row parsing now also rejects every non-object collection element before projecting cells, including when no columns are selected; valid empty arrays and sparse objects remain accepted.

The requested App baseline was reconstructed retrospectively, not represented as tests-written-before-code. Only `QueryBuilderViewModel.cs` was temporarily restored from `b196b175`; a `finally` block restored the exact implementation bytes and verified SHA-256 `4A15FAADDC9F7AEAC8EA3290EDC92B79556726919D9C85A3B696EBF815E46B82`. The bounded subset completed 16 tests: 8 expected failures exposed cycles, malformed later export and the scalar-row gap, while 8 retry/save/valid-envelope controls passed. Because the restored timestamp let the first incremental build retain the baseline DLL, that diagnostic failure was preserved separately and followed by a forced non-incremental rebuild. The final App gate passed 373/373 with zero build warnings/errors; unchanged Core regression remained 29/29. Evidence is under `artifacts/h10c/completion/` as `query-retrospective-red.*`, `app-review-rebuild.log`, `app-review-green.*`, `app-review-stale-output-failure.*`, and `core-review-regression.*`.

Parent full validation at the reviewed working tree then built both complete solutions with `CI=true` in Release and zero warnings/errors. The full Windows App suite (`EnableWebView2=true`) passed 1,482/1,482 and the full Core suite passed 484/484, zero failed/skipped. Evidence is `artifacts/h10c/completion/parent-full-app-{build,test}.log`, `parent-full-app.trx`, `parent-full-core-{build,test}.log` and `parent-full-core.trx`. This remains local validation; main integration, hosted review and merge are pending.

## Integrated H09/H10a validation

H09 head `994a36418cc7fbe3a66e26f0faa5e716b6c2be76`, including current main, H05 and H10a, merged cleanly as `c8ec0043b48fc17f9f7318597124abf1f4e9ca9a`; no source conflict or manual redesign was required. The accepted PR232 test-only synchronization correction `1c1381a863c88d09043d1e710fea0949e7a3ac38` then merged cleanly as `028497224971bdc8cf599167b7e1d2a5785c1463`. Inspection confirms the shared stream, catalog, Dataverse readers and Query retain paging cycle/malformed/incomplete guards together with H10a cancellation/environment ownership and H09 write ownership.

Sequential `CI=true` Release builds passed with zero warnings/errors. Core passed 616/616, and the Windows App with `EnableWebView2=true` passed 1,660/1,660, all with zero failures/skips. Evidence is `artifacts/h10c/integrated-h09/core-{build,test}.log`, `core-test.trx`, `app-{build,test}.log`, and `app-test.trx`. No live service, browser, sign-in, profile-clear or tenant operation was used. H10c remains locally integrated and fully validated; PR creation waits for PR #232 to merge, then current-main integration and hosted review/CI.
**Status:** Integrated and fully validated offline; hosted delivery waits for PR #232.
