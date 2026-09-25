# Metadata response integrity (H06a proposal)

## Status: implemented and focused-validated; parent review, full gates, PR and merge pending

This accepted design is implemented across the owned App/Core parsers, readers, Map Browser warnings and Markdown export.

`ToolBax.Core` `DualWriteMapParser.ParsePage`, `ParseSolutionPage`, and `ParseComponentIdPage`, plus `VirtualTableMetadataParser.Parse`, currently convert malformed/missing envelopes to empty and skip scalar array members. `CoreDualWriteMapReader.PageAll` can then report success/partial earlier pages; `CoreVirtualTableReader` wraps this as `Ok`. `FoToolbox.Core` `DualWriteResponseParser.ParseMaps` accepts bare arrays and `value`/`entities`/`items` aliases, but missing/wrong envelopes become empty and nonobject members are skipped. Map VM ignores solution lookup failure and later map success clears `LoadError`.

## Proposed response boundary

Each parser must distinguish a valid empty collection from an invalid response. Reject malformed/blank JSON, absent/wrong expected collection envelope, scalar/nonobject collection members, and ill-typed continuation metadata. Keep gateway bare/wrapped aliases and their existing priority; preserve optional map fields, H06b Unknown/Ambiguous, legacy aliases, and virtual physical-table filtering. Missing optional scalars in otherwise valid rows remain tolerated. Do not introduce a complete remote schema or require gateway names/versions.

Reader page loads fail as a whole on a malformed first or later page: they must not return successful accumulated pages. Map/virtual failures use existing error result/banner paths; cancellation remains cancellation. Diagnostics name structural location only, never payloads, record IDs, or secrets. Valid empty collections remain success.

The existing gateway non-JSON exception remains the one narrow diagnostic exception: its brief on-screen response fragment helps identify an HTML/proxy interstitial, while existing type-based trace redaction keeps that fragment out of persisted logs. New structural diagnostics added here never include payload fragments or identifiers; H14 owns broader log redaction.

## Nested embedded JSON: accepted decision

Keep the valid row/header/raw text and attach an explicit structural per-row warning when nonempty `msdyn_mapping` or `msdyn_properties` cannot parse as JSON. Never present those detail sections as complete. Missing, null, and blank optional fields remain tolerated; parse the healthy counterpart normally. Preserve raw mapping/properties for the existing inspection path.

Show a load-level count of rows with incomplete details and a selected-row diagnostic. Warnings name field/location only, never raw snippets. A valid refresh clears these warnings. Add a minimal equivalent warning to the existing Markdown map export so an exported row cannot lose the known limitation; this extends ownership only to `DualWriteMapMarkdownExporter` and its tests, without changing H11 streaming/memory design.

Solution-list failure may leave readable maps, but it requires an independent visible nonfatal warning. Only a successful solution reload clears it; successful map loads must not.

## Scope and ownership

Proposed production ownership is the named parsers/readers, a smallest domain parse-error helper/type if needed, Map VM/view warning, `DualWriteMapMarkdownExporter` warning, and corresponding parser/reader/VM/render/export tests. Do not change auth/trust, paging retry/cycle policy, H11 streaming/memory design, or Compare matching.

## Pre-mortem

1. HTML/garbage appears as empty: strict envelope validation.
2. A later bad page appears complete: fail the whole read.
3. Sparse legitimate gateway maps are rejected: retain optional-field semantics.
4. Solution failure is hidden by map success: independent warning lifecycle.
5. Diagnostics leak rows or cancellation is flattened: structural-only diagnostics and cancellation pass-through.

## Acceptance matrix

Cover valid empty and supported aliases; malformed first/later pages; mixed arrays; optional missing fields; normal virtual filtering; nested warning/raw preservation/healthy counterpart; UI/export warning; warning clearing on valid refresh; no partial success; visible solution warning; cancellation; and no live calls.

The completed behavioral baseline executed 16 focused cases and all 16 failed under the prior tolerant parsing behavior. After implementation those cases pass 16/16. Review follow-up adds required component-ID validation, nonblank continuation checks, stacked attached warning layout, and solution-warning retry/retention. Lifecycle RED failed 2/2 for cancelled/disposed retries; corresponding lifecycle/layout GREEN passes 3/3. A final cancellation-ignoring success RED failed 1/1 and its complete retry-lifecycle GREEN passes 5/5. At `b403f977e6a0ae25956dd44adb7607bdc7789d9e`, both Release builds passed with 0 warnings/errors and Core 439/439 passed. At `1857825162e3a4b19fa15b4307e386ee1866b466`, the combined Windows App `CI=true` Release build passed with 0 warnings/errors and App 1,265/1,265 passed, zero failed/skipped. Evidence is stored under ignored `artifacts/h06a/`. No live calls were used; PR, remote review and merge remain pending.

## Accepted PR227 review corrections

Mapping-detail integrity checks will warn structurally, not reject the whole row, when nonempty embedded mapping JSON has a nonobject root, no usable recognized legs array, wrong present collection kinds, or nonobject members in the legs/fieldMappings/valueTransforms structures consumed by the builders. Preserve valid empty arrays, absent optional nested collections/scalars, existing aliases, raw/header data and valid portions. Warnings use a fixed deduplicated set of structural categories, never row snippets or an unbounded per-member list. Existing row/load/selected/Markdown surfaces carry them.

Solution loading captures full EnvironmentIdentity and the existing lifecycle generation. Initial and retry chains must stay in that captured context before picker/warning publication and every following metadata/maps request. Late success/failure from a changed profile or same-ID URL/auth edit is discarded; same-scope ordinary solution failure remains nonfatal, with its independent warning and readable maps. No reactive bus or additional scope feature.

PR227 correction validation: completed behavioral RED `artifacts/h06a/review-red.trx` recorded 22 failures and 5 passing empty/sparse controls. The failures comprise 10 mapping-shape/member cases and 12 gated initial/retry solution-load scope cases (profile switch or same-ID URL/client edit, each with success and exception). Final focused CI=true Release passed 199/199, zero failed/skipped, in `artifacts/h06a/review-final-green.{log,trx}`. It includes parser, Map VM, attached render, Markdown exporter and Core map-reader suites; additional controls cover primitive mapping roots, same-scope thrown solution failure and identity drift during the metadata stage. Existing cancellation/disposal tests remain green. The old healthy recovery fixture now uses the structurally valid empty `legs:[]` shape instead of `{}`. At that focused checkpoint, no new full-suite validation had yet been claimed; prior 190/38 and full 1265/439 results remain historical at their recorded revisions. No live validation was performed.

Warnings use fixed deduplicated categories for mapping root, legs collection/members, fieldMappings collection/members and valueTransforms collection/members. Malformed members are skipped while valid siblings and raw/header/property data survive; no optional scalar schema is imposed. Solution loading returns a current-scope outcome so discarded data cannot advance initialization/retry into metadata or maps for a new identity. The same-scope failure path retains its independent warning and permits maps. Review reply drafts are `artifacts/h06a/review-p1-mapping-shapes.md` and `review-p2-solution-scope.md`.

## Parent validation after PR227 corrections

Parent accepted both behavior fixes at `775bd8ce24035146e66454f63a46a48bf7d8868d`; subsequent commits only reformat the added tests, ending at `b50f4ed7e1cd03c3bbe1f19d05cb54a3ad8d4c09`. At that source the parent CI=true Release App build passed with zero warnings/errors and the full App suite passed 1,299/1,299, zero failed/skipped. Saved evidence: `artifacts/h06a/review-full-app-build.log`, `review-full-app.trx` and `review-full-app-test.log`. The earlier Core 439/439 proof remains applicable: no `src/FoToolbox.Core` or `tests/FoToolbox.Tests` source/test changes occurred since its full run. Focused 199/199 and the behavioral RED evidence above remain recorded. PR227 is open with hosted rerun, rereview and merge pending; this record does not claim those remote gates have completed.