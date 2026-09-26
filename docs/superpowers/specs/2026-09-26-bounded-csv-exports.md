# Bounded CSV exports (H11 Core phase)

**Status:** Core phase accepted and locally validated; App integration/progress remain pending. H11 is incomplete until App and later full gates finish.

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

`CsvExporter.ExportAsync` keeps its public signature and now renders each cell to its existing string/null representation as the row arrives. It records the first-seen case-insensitive canonical header and asynchronously appends one JSON object per row to a private `JsonLineRowSpool`. After the source completes, the spool is flushed and rewound; only then is the caller's CSV writer constructed. Rows are read and rendered one at a time against the final header. `ExportTableAsync` remains unchanged.

The spool uses a GUID-random path with `FileMode.CreateNew`, `FileShare.None`, asynchronous sequential I/O and `FileOptions.DeleteOnClose`. Unix creation requests user read/write mode only; Windows inherits the user's temp-directory access. A narrow internal export entry point accepts a test-owned directory solely for cleanup assertions. Production uses `Path.GetTempPath()`. Neither rows nor paths are logged.

Cancellation is checked before and after source/spool boundaries and before output construction. A producer that returns a page after ignoring cancellation cannot publish it. Source failure or pre-output cancellation leaves the caller stream byte-for-byte unchanged, including no BOM. The caller stream stays open. Once final generic-stream writing starts, failure or cancellation may leave partial bytes; destination atomicity remains the caller's responsibility.

## Runtime evidence

The behavioral baseline was reconstructed from `0eac3ed` by temporarily restoring only `CsvExporter.cs`, building that Core DLL into the evidence directory, and restoring the exact current source bytes in `finally`. The final retention probe lazily produced 20 pages of 25 rows. Baseline retained all 500 row objects and failed the page-relative live-row bound of 25. This was a completed runtime test failure, not a compile-error RED.

Under the spool implementation, the focused suite passes 24/24. It covers retention, one-time cell rendering, late and empty-page columns, sparse and case-variant rows, null/Unicode/formula behavior, BOM/newlines, cumulative rows-read progress, source fault, pre-output and in-flight cancellation, caller stream ownership, and spool cleanup after success/source/output fault/output cancellation. The `CI=true` Release Core solution build compiled both `net10.0` and `net10.0-windows` with zero warnings/errors. Logs and TRX files are under `artifacts/h11/core-phase1/`.

Parent review found one entry-boundary regression introduced when writer construction moved after spooling: null or nonwritable output, null client and null request were no longer rejected before temp creation or source enumeration. Behavioral RED failed 4/4: read-only/null output consumed the producer before rejection, null client surfaced `NullReferenceException`, null request reached the producer, and an invalid output could attempt spool creation first. The correction adds standard null checks and `output.CanWrite` validation at the shared `ExportAsync` core entry before cancellation, spool creation or source enumeration. Public-API tests prove zero producer dispatch and caller stream ownership; the narrow spool seam proves validation precedes any temp-file attempt. `ExportTableAsync` and the CSV/spool contract are unchanged. The complete focused CSV suite now passes 28/28. CI=true Release Core builds for `net10.0` and `net10.0-windows` pass with zero warnings/errors. Review evidence is `artifacts/h11/core-phase1/review-core-net10-build.log`, `review-core-net10-windows-build.log`, `review-csv-tests.log` and `review-csv-tests.trx`.

## Capacity and cleanup limits

Peak exporter-owned memory scales with the discovered column set, current rendered row/JSON line and the current input page supplied by the client. It is not a universal constant. Spool disk usage scales with the rendered result size. Temporary rows are plaintext under the current user's temp access while the export runs. `DeleteOnClose` and deterministic disposal provide best-effort cleanup for normal and handled exceptional paths; there is no secure wiping or exceptional-process crash guarantee. App Query Builder integration and user-visible progress remain outside this Core phase.
