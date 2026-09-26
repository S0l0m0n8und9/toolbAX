# Dataverse paging cycles (H10c App-reader phase)

**Status:** Implemented and locally validated; uncommitted pending parent review.

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
