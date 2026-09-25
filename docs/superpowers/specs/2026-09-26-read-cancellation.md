# Read cancellation (H10a)

## Accepted scope and phases

Phase 1 is read-service cancellation only. Phase 2 UI/loader integration follows parent review; H10a is not complete until both phases are reviewed and validated. Preserve full-identity/origin guards, malformed-response contracts and existing non-caller timeout diagnostics. No automatic retries, paging-cycle/export work, shared-catalog redesign, auth/native changes, Trace-policy changes, Profiles/ConnectionTester changes or mutation evidence/replay changes.

Caller cancellation remains OperationCanceledException-compatible. Check the caller token before auth/network/cache mutation, after async boundaries and immediately before parsing, publication, cache commits or another request. In exception paths caller cancellation takes precedence even when an ignoring dependency throws another exception. A timeout-shaped cancellation with a live caller token retains existing failure behavior. Cancellation does not roll back work already performed.

CoreDataverseClient must not translate caller cancellation into 401/ordinary failure or Trace failure. CoreMetadataService must not commit canceled entity/field/navigation/enum results from an ignoring catalog and must preserve prior valid cache. Map/virtual readers stop paging/count follow-ons and logical-name-cache changes; no partial-success/false-empty results. HttpODataClient streaming checks cancellation before yielding/advancing and must not wrap caller cancellation in a plugin-friendly error. Gateway changes are restricted to the GET branch; H03 mutation dispatch/evidence/deadline/buffer behavior remains exact.

## Five pre-mortems

1. Auth cancellation becomes a 401 failure: propagate caller cancellation before failure translation/Trace.
2. An ignoring dependency returns late success: guard after awaits before parsing/publication.
3. Cancellation between pages or count phases starts another call: guard every follow-on boundary.
4. Canceled metadata poisons facade/logical-name caches: guard commits and preserve the prior valid cache.
5. A real timeout is mistaken for user cancellation: include live-token timeout negative controls.

## Verification

Gated fake auth/HTTP/catalog/readers, no sleeps/live services. Completed behavioral RED/GREEN must cover pre-canceled zero dispatch/cache change, canceled auth/body/read, ignored success/exception, streaming exception compatibility, map/solution/component/count boundaries, metadata cache preservation, virtual-table outcomes and existing origin/malformed/identity controls. Run unchanged H03 mutation regression coverage. Save actual focused logs/TRX under artifacts/h10a; no full suite or remote actions. Commit exact phase-one files and freeze for parent review before UI work.

## Phase 1 source-ready checkpoint

Read services now give requested caller cancellation precedence before dispatch, after asynchronous boundaries and before parsing/publication/cache mutation. Dataverse cancellation bypasses 401/ordinary-failure conversion and RequestTrace failure publication. Metadata checks before cache preparation and under the cache-commit lock preserve prior valid facade data when an ignoring catalog completes after cancellation. Virtual/map readers discard canceled responses, prevent later page/component/count requests and guard logical-name cache insertions/removals. Streaming OData retains OperationCanceledException-compatible results instead of wrapping caller cancellation and checks before yielding/advancing. Gateway GET checks cover response/body and public read parsing; the mutation implementation and diagnostic tail were verified text-identical after line-ending normalization.

Completed baseline RED: App read-service regressions failed 28/29 while the real HTTP-timeout control passed (`services-red.trx`); Core transport regressions failed 8/9 while their real timeout control passed (`transport-red.trx`). A deterministic cancellation during gateway response cleanup reproduced one remaining await-to-parse publication failure (`gateway-parse-boundary-red.trx`), then the GET parse guard closed it.

Final CI=true Release focused GREEN passed App 129/129 and Core 43/43, zero failed/skipped (`artifacts/h10a/services-final-green.{log,trx}` and `transport-final-green.{log,trx}`). Coverage includes pre-cancel zero dispatch/cache reset, late success/other exception, streaming body/yield/advance, map/solution/component later pages without partial success, count phase/cache preservation, canceled entity/field/navigation/enum loads, live-token auth/HTTP timeouts, existing malformed/origin/identity controls and unchanged H03 mutation regressions. No public signatures or result DTOs changed. No full suites, live calls, packages or remote operations.

Phase 1 is frozen for parent review. H10a remains incomplete: CoreODataClient GET, EntityCatalogLoader and read-only UI publication/follow-on integration belong to the separately authorized phase 2. Cancellation does not roll back work performed before the token was observed; the shared catalog is unchanged.
