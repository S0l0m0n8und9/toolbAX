# Query paging integrity (H10c)

Status: Implemented and fully validated after final H09 cache-contract integration; pending parent review and hosted delivery.

## Contract

Track successfully committed request targets per result set using ordinal escaped HTTP request keys. Resolve relative requests exactly as CoreODataClient does: append to the captured normalized F&O endpoint, preserving any proxy prefix. System.Uri normalizes scheme, host and default port and removes fragments from the HTTP key; preserve path/query case and query order. With no real profile/base, retain ordinal relative targets rather than inventing a host. Keep the client's origin guard.

Run publishes fresh page history only with its successful current result. LoadMore checks committed history before dispatch and adds a new target only at guarded successful commit. A repeated returned continuation keeps the current page's unique rows but stops paging, clears NextLink and reports incomplete results. Failure/cancellation/supersession never commits a visited target. Clearing, entity changes and a successful new Run reset history; identity/result/read ownership guards apply equally to rows and history.

ExportAll has a separate traversal history and refuses a cycle before dispatch or file save. Preserve the existing 500-page ceiling and qualified saved result for genuinely unique capped pages. Do not alter row memory, CSV escaping, file-save commit semantics, retries, auth, logging or global URL handling. Caller cancellation still wins; a saved file is never retroactively called cancelled.

An explicitly present non-null @odata.nextLink must be a nonblank string. Reject wrong types and empty/whitespace strings with a static InvalidOperationException-compatible incomplete-paging message, without echoing URLs/query/rows. Absent/null is terminal. Existing count handling remains unchanged.

## Five pre-mortems

1. A cycle duplicates rows: recognize initial self-links and A-B-A using committed HTTP request keys before duplicate dispatch/append.
2. A stale response corrupts new history: publish history only under the same identity/generation/read-owner guards as rows.
3. An HTTP retry falsely looks cyclic: failed/cancelled pages never enter successful-page history.
4. Opaque case-sensitive tokens collide: ordinal keys preserve path/query case and query order; only URI authority/default-port/fragment normalization is applied.
5. ExportAll saves cyclic partial data as complete: independent traversal history stops with an explicit incomplete/no-file-saved result before SaveTextAsync.

## Validation

Gated and bounded fake transport tests observe calls, rows, status and saves: self-link, A-B-A, relative/absolute aliases including proxy prefixes, token case distinction, successful pages/late columns, failure and cancellation retry, reset/rerun/overlap isolation, invalid continuations, export cycle with zero saves and unique-page cap qualification. Retain H10a cancellation and committed-file truth controls. No sleeps/live calls/packages/remote operations. Source/tests first; shared build slot belongs to the parent until released. Capture baseline RED by temporarily reverting only the owned Query production file, restore it, then run focused Query/render/H10a compatibility GREEN. Full gates remain parent-owned.
# Prior stopped checkpoint

The existing Query paging partial is unvalidated and must not be treated as approved or complete. Before H10c completion, tests must prove a malformed collection envelope on a later page causes explicit failure and zero `SaveTextAsync` calls; a valid empty final page succeeds; a malformed first page remains an honest failure; and cancellation/identity ownership still wins. These are acceptance requirements only, not authorization to edit source.

## Completion after approval

The partial implementation was retained and completed. Result history contains canonical request targets only after a guarded successful commit; failed/cancelled loads can retry the same target, reruns replace prior history, repeated returned continuations retain the newly committed unique rows while clearing `NextLink`, and case-distinct tokens remain distinct. The request-key rule appends relative targets to the captured normalized F&O endpoint, including proxy prefixes, and retains raw ordinal targets when there is no real base.

`ExportAllCsv` uses independent traversal history. A repeated target stops before repeat dispatch or save; blank/missing/non-array collection envelopes and invalid present continuations fail explicitly; a valid empty final page completes; and the existing 500-unique-page result remains saved with its incomplete qualifier. Syntactically invalid JSON continues to throw through the existing parsing path. Row buffering and file-save commit semantics are unchanged.

Focused bounded controls cover self-link, A-B-A, proxy alias, case distinction, failure retry, rerun replacement, malformed first/later pages, valid empty final page, export cycle with zero save, late columns, and the 500-page qualifier. The final sequential App gate also includes render, cancellation/supersession and committed-save truth controls and passed 367/367 under `CI=true` Release. Evidence is `artifacts/h10c/completion/app-focused.{log,trx}`.

## Parent-review corrections

The outer export catch no longer interprets exception text from `SaveTextAsync` as proof that no file was saved. A throwing fake proves the save service was invoked and that an `InvalidOperationException("incomplete write")` produces a generic failure without cancellation or rollback claims; the existing committed-save success control remains green. Pre-save cycle exits retain the explicit no-file wording, and malformed pages retain their incomplete diagnosis with zero save calls.

`ParseRows` now requires every `value` element to be an object before projecting selected or derived cells. This closes the zero-column scalar hole while preserving valid empty arrays and sparse object rows. Additional controls prove a caller-cancelled LoadMore can retry the same continuation, and that a relative continuation plus its absolute alias resolve to one proxy-prefixed request identity.

Retrospective baseline verification temporarily restored only `QueryBuilderViewModel.cs` from `b196b175`, retained the current tests and other production files, and restored exact source bytes in `finally`. It completed 16 bounded tests with 8 expected failures and 8 controls passing. This is retrospective verification, not test-first RED. After a forced rebuild to avoid the restored timestamp reusing the baseline DLL, the final focused App gate passed 373/373; Core regression passed 29/29. Evidence is in `artifacts/h10c/completion/query-retrospective-red.*`, `app-review-green.*`, and `core-review-regression.*`.

## Integrated validation

H09/H10a and the PR232 test synchronization correction merged without Query source conflicts. Query still commits page history only after guarded success, permits failed/cancelled-page retry, rejects repeated/malformed pages without false whole-export success, and saves at the 500-unique-page cap only with the explicit incomplete qualifier. H10a caller/lifecycle ownership and committed-file truth remain intact. The integrated `CI=true` Release App WebView2 build passed with zero warnings/errors and the full App suite passed 1,660/1,660; integrated Core passed 616/616. Evidence is `artifacts/h10c/integrated-h09/`. No live OData request or file destination was used.

Combined PR232 review head `b1caddb15f29721b1653d3241c59405616712558` then merged without Query source changes. Core remains unchanged at 616/616; the rebuilt App WebView2 solution had zero warnings/errors and passed 1,668/1,668. Evidence is `artifacts/h10c/integrated-h09-review/`. No live request or file destination was used.

Final PR232 head `1890a871c864532b7c2787580eb51e3d6f04f30d` merged cleanly as `7df7f26e4aca9e48cefdfa9301e422c6bff003dd` with no Query conflict or source change. Query still owns independent page history, retries failed/cancelled pages, rejects cycles and malformed success envelopes, withholds partial exports, and preserves the qualified 500-unique-page ceiling. Parent verified Core source/tests unchanged from the 616/616 gate. The rebuilt `CI=true` Release App WebView2 solution had zero warnings/errors and passed 1,670/1,670 with zero failures/skips. Evidence is `artifacts/h10c/integrated-h09-cache-contract/`. No live OData request or file destination was used; hosted delivery still waits for PR #232 merge and current-main ancestry integration.
