# Metadata response integrity (H06a proposal)

## Status: accepted; implementation not started

This design is accepted. Implementation, tests, tracker, and remote state changes have not started.

`ToolBax.Core` `DualWriteMapParser.ParsePage`, `ParseSolutionPage`, and `ParseComponentIdPage`, plus `VirtualTableMetadataParser.Parse`, currently convert malformed/missing envelopes to empty and skip scalar array members. `CoreDualWriteMapReader.PageAll` can then report success/partial earlier pages; `CoreVirtualTableReader` wraps this as `Ok`. `FoToolbox.Core` `DualWriteResponseParser.ParseMaps` accepts bare arrays and `value`/`entities`/`items` aliases, but missing/wrong envelopes become empty and nonobject members are skipped. Map VM ignores solution lookup failure and later map success clears `LoadError`.

## Proposed response boundary

Each parser must distinguish a valid empty collection from an invalid response. Reject malformed/blank JSON, absent/wrong expected collection envelope, scalar/nonobject collection members, and ill-typed continuation metadata. Keep gateway bare/wrapped aliases and their existing priority; preserve optional map fields, H06b Unknown/Ambiguous, legacy aliases, and virtual physical-table filtering. Missing optional scalars in otherwise valid rows remain tolerated. Do not introduce a complete remote schema or require gateway names/versions.

Reader page loads fail as a whole on a malformed first or later page: they must not return successful accumulated pages. Map/virtual failures use existing error result/banner paths; cancellation remains cancellation. Diagnostics name structural location only, never payloads, record IDs, or secrets. Valid empty collections remain success.

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
