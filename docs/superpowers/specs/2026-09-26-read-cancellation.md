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

## Accepted phase 2 scope and pre-mortems

Phase 2 adds GET-only guards to CoreODataClient, explicit caller/lifetime/supersession handling in EntityCatalogLoader, and read-boundary publication/follow-on guards in Metadata, Virtual Tables, Map, Query and Post metadata-loading paths. H03 mutations/readback evidence and Post send cancellation ownership remain unchanged. Metadata/Virtual/Map receive one reachable read Cancel control each, covering their actual initial/manual/field/count work. No Profiles/Shell host hookup is duplicated from H08a.

1. A late GET becomes success/failure/Trace after cancellation: conditional read-only transport guards, with mutation controls unchanged.
2. An old loader owner overwrites newer state after A-B-A: linked-token and request-reference guards before getters, LastError or success.
3. Canceled UI reads still paint rows/fields/errors: check tokens plus existing lifecycle/identity/selection guards after every await.
4. Cancellation between count legs or pages starts another read/save: check before every follow-on and before export file-save invocation.
5. An old completion lowers a newer busy indicator: keep sequence/owner-based cleanup, never unconditional cross-owner clearing.

Attached headless tests must prove visible Cancel controls cancel actual gated reads without late painting. A live-token timeout remains an ordinary failure; caller cancellation stays quiet and preserves prior valid data. EntityCatalogLoader keeps its null/false cancellation contract and never abandons an unobserved task via WaitAsync. Query ExportAll checks cancellation through HTTP/data preparation and immediately before SaveTextAsync, but does not reclassify an actually committed file as cancelled afterward. ExportCsvFile, Markdown, clipboard behavior and H11 export memory/commit redesign remain outside this phase.
## Phase 2 source-ready checkpoint

CoreODataClient now checks caller cancellation conditionally for GET before auth/dispatch and after auth/send/body boundaries, including generic-error translation and Trace publication. Mutation dispatch, deadline, buffer and response-evidence behavior is unchanged. EntityCatalogLoader keeps null/false cancellation results, retains ownership of entity/field fetches, rejects canceled/superseded results before getters or LastError publication, and reports timeout-shaped exceptions when its linked token remains live. No task is abandoned with WaitAsync in production.

Metadata, Virtual Tables, Map, Query and Post metadata reads guard publication and follow-on requests. Per-read ownership prevents older completions clearing newer busy/count state. Existing full-identity, selection and disposal guards remain in data/error paths. Metadata/Virtual/Map expose one read Cancel button, tested attached to actual windows and gated reads; the Map button also cancels a count before its F&O follow-on. Previously completed pinned-environment counts remain visible while subsequent legs are stopped. Query HTTP paging/preparation checks cancellation before file save; a fake that cancels during a completed SaveTextAsync still reports Saved. Post send/readback, file/clipboard export paths and host shutdown wiring are unchanged.

Completed phase-2 baseline RED against committed phase-1 source: 42 failed and 7 controls passed out of 49 (`artifacts/h10a/phase2-expanded-red.{log,trx}`). The same 49 cases passed with the fix (`ui-boundaries-green.{log,trx}`); two further cache-supersession/attached-count controls are included in the final gate. The earlier loader/UI RED was 15/15 failing (`ui-loader-red.{log,trx}`), followed by 15/15 green. All temporary baseline reversions were restored before final validation.

Final phase-2 CI=true Release focused gate passed 515/515, zero failed/skipped (`artifacts/h10a/phase2-final-green.{log,trx}`). It includes the new GET/UI boundary cases, existing loader/Metadata/Virtual/Map/Query/Post tests, attached render suites, CoreODataClient and unchanged H03 write-evidence/WriteOutcome regressions. The existing Query export cancellation test now cancels its actual caller token; a separate live-token timeout control requires failure. No live calls, package changes, full suites or remote actions. Phase-1 App129/Core43 evidence remains separate and unchanged. Parent phase-2 review, integration and complete-item gates are pending; H10a is not yet claimed delivered.
## Map concurrent-reload verification

The proposed gap that visible Cancel might leave an older concurrent ReloadMaps transport uncancelled was disproved against unchanged production source at `b196b17516e34fce96e141fa820a7882be639d76`. With the pinned CommunityToolkit.Mvvm 8.4.2, starting a second token-aware ExecuteAsync invocation cancels the previous invocation's token even when AllowConcurrentExecutions is true. Both gated regression cases explicitly recorded `Older token canceled by second invocation: True`. Attached read Cancel and Dispose each then canceled the latest token. Both reader tokens were canceled; late success and a different late exception changed neither rows nor the prior error, no follow-on call occurred, and pending ownership settled after both awaited operations unwound. Live-view busy state remained set until the final unwind. A disposed view's existing internal busy-flag behavior is unchanged.

Both controls passed 2/2 on the original production source (`artifacts/h10a/map-overlap-baseline.{log,trx}`). This is verification of existing cancellation behavior, not a production bug fix; no private cancellation registry was added. The already-started focused compatibility run also completed 517/517, zero failed/skipped (`artifacts/h10a/phase2-overlap-green.{log,trx}`), comprising the prior 515 cases plus these two controls. Parent accepted the disproved finding; full App/Core gates remain with the parent.
## Final local validation

Parent final source/test review accepted at `5f77e3c91598ee634879372c88d54f69d84f770b`, including unchanged H03 mutation paths, truthful reporting after file-save commit and the disproved concurrent Map reload concern. Parent full offline App passed 1,454/1,454 and Core passed 466/466, zero failed/skipped. Both CI=true Release solution builds had 0 warnings/errors; Windows App used EnableWebView2=true. Core production/test code was unchanged between its `b196b175` build and the test-only `5f77e3c` checkpoint.

Evidence: `artifacts/h10a/parent-full-{app,core}-{build,test}.log` and `parent-full-{app,core}.trx`. Earlier RED/focused evidence above remains retained. No live backend, authentication or browser session was used. H10a is locally validated; publication, hosted CI/Greptile review and merge remain pending.

## Integrated main validation

Main `573a6a42dc0d2f818ef4c5b3c904713cd9405ec4` (including delivered H05 and H08a) merged cleanly into the H10a branch as `800a9683ba94258062516554b9c9b6c4b76f6924`; no source or tracker conflict required manual resolution. The resulting source diff against main remains limited to H10a cancellation ownership plus its tests and delivery documentation, with no H05 authentication or H08a profile-test rollback.

Sequential `CI=true` Release gates passed with zero warnings/errors: Core built and passed 582/582, then the Windows App built with `EnableWebView2=true` and passed 1,589/1,589, with zero failures/skips. Evidence is `artifacts/h10a/integrated-h05/core-{build,test}.log`, `core-test.trx`, `app-{build,test}.log`, and `app-test.trx`. No live backend, browser, sign-in or profile-clear action was used. H10a remains ready for local documentation commit, publication and hosted PR review/CI; merge remains pending.
