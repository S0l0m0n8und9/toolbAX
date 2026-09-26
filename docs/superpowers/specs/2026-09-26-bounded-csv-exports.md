# Bounded CSV exports (H11 Core phase)

**Status:** Core and App phases are accepted and fully locally validated through the merged final H09 cache contract. PR creation waits for H10c to reach main.

## Scope

Preserve `ExportAsync`, header union/order, BOM/newline/escaping/formula guards, and rows-read progress. Replace retained row objects with one temporary JSONL spool per export: render current cell strings once, append asynchronously, then read one record at a time after final headers are known. Retain only schema/current row/current input page.

Use unique `CreateNew`, `FileShare.None`, `DeleteOnClose`, user-only Unix mode where supported, and inherited user temp access on Windows. Never log rows or temp paths. Cleanup on success, source failure, cancellation, or output failure. Source spooling completes before writer construction/output, so pre-output failure/cancel leaves caller stream unchanged. Generic output stream failures/cancellation may leave partial bytes; caller owns destination atomicity. Stream remains caller-owned/open.

## Pre-mortem

1. Late header drops data: retain complete first-seen case-insensitive union before rendering.
2. All rows remain retained: spool rendered string pairs only and test weak-reference/live-row bounds.
3. Source/cancel touches destination: delay output construction until spool/cancellation boundary.
4. Temp rows leak on failure: narrow deterministic temp seam and cleanup tests.
5. Generic output cancellation promises atomicity: document partial-byte boundary and caller ownership.

## Acceptance

Cover lazy multi-page retention, late/empty headers, Unicode/formula strings, progress, unchanged pre-output destination on source error/cancel, caller stream ownership, output failure cleanup, cancellation-ignoring source, and narrow temp cleanup. No HTTP/auth/UI changes or live calls.

## Implemented Core boundary

`CsvExporter.ExportAsync` keeps its public signature and now renders each cell to its existing string/null representation as the row arrives. It records the first-seen case-insensitive canonical header and asynchronously appends one JSON object per row to `CsvRowSpool`. After the source completes, the spool is flushed and rewound; only then is the caller's CSV writer constructed. Rows are read and rendered one at a time against the final header. `ExportTableAsync` remains unchanged.

The spool uses a GUID-random path with `FileMode.CreateNew`, `FileShare.None`, asynchronous sequential I/O and `FileOptions.DeleteOnClose`. Unix creation requests user read/write mode only; Windows inherits the user's temp-directory access. A narrow internal export entry point accepts a test-owned directory solely for cleanup assertions. Production uses `Path.GetTempPath()`. Neither rows nor paths are logged.

Cancellation is checked before and after source/spool boundaries and before output construction. A producer that returns a page after ignoring cancellation cannot publish it. Source failure or pre-output cancellation leaves the caller stream byte-for-byte unchanged, including no BOM. The caller stream stays open. Once final generic-stream writing starts, failure or cancellation may leave partial bytes; destination atomicity remains the caller's responsibility.

## Runtime evidence

The behavioral baseline was reconstructed from `0eac3ed` by temporarily restoring only `CsvExporter.cs`, building that Core DLL into the evidence directory, and restoring the exact current source bytes in `finally`. The final retention probe lazily produced 20 pages of 25 rows. Baseline retained all 500 row objects and failed the page-relative live-row bound of 25. This was a completed runtime test failure, not a compile-error RED.

Under the spool implementation, the focused suite passes 24/24. It covers retention, one-time cell rendering, late and empty-page columns, sparse and case-variant rows, null/Unicode/formula behavior, BOM/newlines, cumulative rows-read progress, source fault, pre-output and in-flight cancellation, caller stream ownership, and spool cleanup after success/source/output fault/output cancellation. The `CI=true` Release Core solution build compiled both `net10.0` and `net10.0-windows` with zero warnings/errors. Logs and TRX files are under `artifacts/h11/core-phase1/`.

Parent review found one entry-boundary regression introduced when writer construction moved after spooling: null or nonwritable output, null client and null request were no longer rejected before temp creation or source enumeration. Behavioral RED failed 4/4: read-only/null output consumed the producer before rejection, null client surfaced `NullReferenceException`, null request reached the producer, and an invalid output could attempt spool creation first. The correction adds standard null checks and `output.CanWrite` validation at the shared `ExportAsync` core entry before cancellation, spool creation or source enumeration. Public-API tests prove zero producer dispatch and caller stream ownership; the narrow spool seam proves validation precedes any temp-file attempt. `ExportTableAsync` and the CSV/spool contract are unchanged. The complete focused CSV suite now passes 28/28. CI=true Release Core builds for `net10.0` and `net10.0-windows` pass with zero warnings/errors. Review evidence is `artifacts/h11/core-phase1/review-core-net10-build.log`, `review-core-net10-windows-build.log`, `review-csv-tests.log` and `review-csv-tests.trx`.

## Capacity and cleanup limits

Peak exporter-owned memory scales with the discovered column set, current rendered row/JSON line and the current input page supplied by the client. It is not a universal constant. Spool disk usage scales with the rendered result size. Temporary rows are plaintext under the current user's temp access while the export runs. `DeleteOnClose` and deterministic disposal provide best-effort cleanup for normal and handled exceptional paths; there is no secure wiping or exceptional-process crash guarantee.

## App phase 2 locked boundary

The App reuses the accepted row spool as public `CsvRowSpool`; it does not introduce a second persistence format. The spool captures a `StringComparer` at creation: Core retains `OrdinalIgnoreCase`, while Query Builder uses `Ordinal` because OData property names and the H10c request history are case-sensitive. A narrow App `QueryCsvSpool` owns the rendered-row spool plus one completed CSV temporary stream. It appends each current `QueryResultRow` as raw strings, stages the entire final CSV only after paging and the final header union complete, then lends the readable stream to `IFileSaveService.SaveStreamAsync`. The spool owner disposes both streams after save, cancel or failure.

`QueryCsv` gains a streaming writer that reuses its existing escape function and exactly preserves current bytes: UTF-8 BOM; ordinal columns; CRLF before each row; no trailing newline; null/absent values as empty cells; genuine em-dash values preserved. Existing `Build`, preview clipboard export and text-save paths remain unchanged.

The production save service validates a readable borrowed stream before opening the picker. After a pick it checks cancellation before `OpenWriteAsync`; past destination truncation it completes a bounded `CopyToAsync` with `CancellationToken.None` and returns the actual path. It never materializes a stream into text and never closes the caller's source stream. A late cancellation after destination opening may not interrupt the committed copy. Generic copy failure may leave partial output and must not be described as rollback or “no file saved.”

Query Export All retains H10c request identity, strict page validation, cycle detection, captured environment/read ownership and the existing 500-unique-page qualified cap. Each accepted row is appended to disk immediately. Status reports cumulative rows/pages after each completed page, then `Preparing CSV…`, then the existing saved/cancelled/failure outcome. Scope, owner and cancellation guards surround append, staging and save boundaries. Cycle, malformed/source failure and pre-save cancellation invoke no save. Once save returns a committed path, a late cancellation remains Saved.

### App phase 2 five failure pre-mortems

1. **A late column is lost because early rows were rendered against an incomplete header.** Retain the first-seen final column union separately, spool raw named cells, and render only after all accepted pages are known.
2. **The implementation moves rows to another `List` or materializes the whole CSV string.** Use the actual spool in a large lazy multi-page ViewModel test with weak-reference/page-relative retention and a streaming save sink.
3. **The destination opens before the source is proven complete.** Stage the complete CSV temp stream first; cycle, malformed page, source failure and pre-save cancellation assert zero save calls.
4. **Late identity/cancellation changes publish progress or misreport save state.** Recheck captured lifecycle/read ownership around every async append, stage and save boundary; preserve H10a committed-save truth.
5. **Temporary files leak or streaming formatting drifts from `QueryCsv.Build`.** Assert owned-directory cleanup on success/fault/cancel and byte-for-byte equivalence for Unicode, formula guards, null, genuine em-dash, ordinal case variants, late headers and empty pages.

### Verified premises before App source

- H10a/H10c are integrated locally at `5c0686a8adec7fafbfb7527cdb94614c340d39e7`; current Query Export All already rejects cycles/malformed envelopes before save, preserves identity/read-owner guards and qualifies the 500-page result.
- Core spool entry validation and cleanup are accepted with 28/28 focused tests and both Core target frameworks building cleanly.
- Existing `QueryCsv.Build` defines the App's exact CRLF/no-trailing-newline, formula, null and em-dash semantics.
- Existing `StorageFileSaveService` checks cancellation before `OpenWriteAsync` for text saves; the stream path must preserve that boundary and complete copying after open.
- Main through PR229 is integrated at `db0a8ca`; H08a, H10a, H10c and H11 tracker rows are preserved.

## App phase 2 implementation and focused evidence

The accepted Core helper is now public `CsvRowSpool` with a comparer captured at creation. Core continues to use the default `OrdinalIgnoreCase`; the App's `QueryCsvSpool` supplies `Ordinal`, appends raw current-row strings, and owns both the JSONL row spool and completed CSV temporary stream. `QueryCsv.WriteAsync` produces byte-identical UTF-8 BOM/CRLF/no-trailing-newline output using the existing escape function. `QueryCsv.Build`, preview clipboard export and text saves are unchanged.

`IFileSaveService.SaveStreamAsync` carries a borrowed readable stream. `StorageFileSaveService` validates it before picker access, checks cancellation before `OpenWriteAsync`, then copies with a bounded buffer and `CancellationToken.None` after truncation. The caller's stream remains open. The fake decodes only small successful fixtures for existing assertions. A late cancel after destination opening completes and reports the saved path; copy failure remains a generic failure without rollback claims.

Query Export All no longer retains an all-row list or complete CSV string. It keeps only the final ordinal column union/current page, appends rows asynchronously, publishes cumulative rows/pages after each accepted page, stages the complete CSV, reports `Preparing CSV…`, and calls the stream save API. H10c visit history, strict malformed/cycle behavior, identity/read ownership, cancellation, no-save failures and the qualified 500-page cap are retained. H10a's committed-save truth remains green.

The public ViewModel RED completed at pre-App commit `7a2fc90`: after page one, status remained `Exporting all rows…` instead of reporting one row/one page. Exact implementation bytes were restored in `finally`; proof is `artifacts/h11/app-phase2/progress-red.{log,trx}`. GREEN covers byte equivalence for late ordinal case variants, Unicode, formula, null and genuine em-dash; actual helper weak-reference retention; large 30-page/1,200-row ViewModel export into a bounded streaming sink; page/preparing progress; temp cleanup; pre-open and late cancellation; caller stream ownership; zero save on cycle/malformed/cancel; cap qualification; save-fault honesty; H10a/H10c regressions and unchanged preview/render behavior.

Parent review found one save-truth edge: a late caller cancellation could mask a non-cancellation disk/copy failure thrown after `SaveStreamAsync` was invoked. Export All now records the save invocation boundary; non-OCE failures after it remain `Export failed` even if the caller token becomes cancelled. Clean pre-save/OCE cancellation, picker cancellation and completed-save truth remain unchanged. The storage helper leaves open cancellation unchanged, but wraps every `OperationCanceledException` from copy, flush or disposal after a destination opens as `IOException`, because cancellation is intentionally disabled past truncation. Runtime RED failed those two truth cases while the two final-CSV cleanup controls passed; corrected GREEN passed 4/4. Cancellation and header faults triggered after final CSV temp creation prove both owned temp files are removed on disposal.

Final sequential `CI=true` Release builds completed with zero warnings/errors: Core built `net10.0` and `net10.0-windows`, then App built successfully. Focused Core CSV passed 28/28; final focused App Query/stream/storage/H10a/H10c compatibility passed 311/311, with no failures/skips. Evidence is under `artifacts/h11/app-phase2/` as `core-build.log`, `core-csv-green.{log,trx}`, `app-build.log`, `app-focused-green.{log,trx}`, `app-review-build.log`, and `app-review-green.{log,trx}`. No full suite, live call, push or final source commit was performed.

## Latest-main integration and complete gates

The accepted App source checkpoint is `b2f75d3`. Main `728df34fd44367edf3db6bc7f781693953bb184c`, containing merged H05 and H10a PR231, merged cleanly as `7033826`; H10c paging and H11 spooling/save truth remain present. The integration preserved H05 delegated-binding/native-close behavior, H10a cancellation and committed-save truth, H10c strict paging/cycle/cap behavior, and the H11 tracker row.

After integration, sequential `CI=true` Release builds completed with zero warnings/errors: Core built both target frameworks and App built with `EnableWebView2=true`. The complete Core suite passed 615/615 and the complete App suite passed 1,632/1,632, with no failures or skips. Evidence is `artifacts/h11/integrated-h10a/core-{build,test}.log`, `core-full.trx`, `app-{build,test}.log` and `app-full.trx`. No live service, browser, sign-in, profile-clear, gateway or Power Platform call occurred.

H11 is ready for delivery review, but its PR must wait until H10c is on main because Query Export All relies on H10c's strict collection parsing, cycle identity and qualified 500-page cap. No push or PR was created.

## Published H09 review integration

Published H09 review head `b1caddb15f29721b1653d3241c59405616712558` merged cleanly into the final H11 documentation checkpoint `a65b5e29ec7cf00ffeb70dfc449c280c4b225348` as `4a073d0a56f4786c076d4c72ef735b301ea9a945`. Git resolved the tracker automatically and preserved H10c paging plus H11 spool, stream-save, byte-format, cancellation and save-truth behavior. There were no conflicts and no H11 production changes or redesign.

Sequential `CI=true` Release gates passed after the merge. Core built both target frameworks with zero warnings/errors and its complete suite passed 631/631. App built with `EnableWebView2=true` with zero warnings/errors and its complete suite passed 1,681/1,681. Evidence is under `artifacts/h11/integrated-h09-review/` as `core-{build,test}.log`, `core-full.trx`, `app-{build,test}.log` and `app-full.trx`.

Validation used local stores and deterministic tests only. No live service, authentication, browser, sign-in, profile-clear, gateway or Power Platform call occurred. This checkpoint intentionally does not integrate any newer H09 cache-contract correction; H11 remains unpublished until final H09 and H10c reach `main`.

## Final H09 cache-contract integration

Final H09 cache-contract head `1890a871c864532b7c2787580eb51e3d6f04f30d`, whose tree is now merged on `main` as `ae537ee8b0a8cf357155929d40b6d8fb8ce97924`, was integrated into reviewed H11 checkpoint `291a154cbed56fce8d828bd25620b9b085eaa7a2` as `5e835c1b0817584298ddc9c559906c39f9d00a5c`. The merge had no source conflict. `1890a87` is an ancestor of the result, and `git diff 291a154..5e835c1 -- src/FoToolbox.Core tests/FoToolbox.Tests` is empty, so the reviewed 631/631 Core gate remains applicable.

The final `CI=true` Release App build with `EnableWebView2=true` passed with zero warnings/errors, and the complete App suite passed 1,683/1,683 with no failures or skips. Evidence is `artifacts/h11/integrated-h09-cache-contract/app-build.log`, `app-test.log` and `app-full.trx`.

Validation used local stores and deterministic tests only. No live service, authentication, browser, sign-in, profile-clear, gateway or Power Platform call occurred. H11 now waits only for dependent H10c to reach `main` before publication and hosted review.
