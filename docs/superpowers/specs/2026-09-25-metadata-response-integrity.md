# Metadata response integrity (H06a proposal)

## Status and verified baseline

This is a proposal only. It changes no parser, reader, VM, test, tracker, or remote state.

`ToolBax.Core` `DualWriteMapParser.ParsePage`, `ParseSolutionPage`, and `ParseComponentIdPage`, plus `VirtualTableMetadataParser.Parse`, currently convert malformed/missing envelopes to empty and skip scalar array members. `CoreDualWriteMapReader.PageAll` can then report success/partial earlier pages; `CoreVirtualTableReader` wraps this as `Ok`. `FoToolbox.Core` `DualWriteResponseParser.ParseMaps` accepts bare arrays and `value`/`entities`/`items` aliases, but missing/wrong envelopes become empty and nonobject members are skipped. Map VM ignores solution lookup failure and later map success clears `LoadError`.

## Proposed response boundary

Each parser must distinguish a valid empty collection from an invalid response. Reject malformed/blank JSON, absent/wrong expected collection envelope, scalar/nonobject collection members, and ill-typed continuation metadata. Keep gateway bare/wrapped aliases and their existing priority; preserve optional map fields, H06b Unknown/Ambiguous, legacy aliases, and virtual physical-table filtering. Missing optional scalars in otherwise valid rows remain tolerated. Do not introduce a complete remote schema or require gateway names/versions.

Reader page loads fail as a whole on a malformed first or later page: they must not return successful accumulated pages. Map/virtual failures use existing error result/banner paths; cancellation remains cancellation. Diagnostics name structural location only, never payloads, record IDs, or secrets. Valid empty collections remain success.

## Nested embedded JSON

Nonempty malformed embedded `msdyn_mapping`/`msdyn_properties` JSON must not present as confidently complete detail. Propose a safe explicit diagnostic retaining raw text for the existing detail path. The implementation decision remains open: all-or-nothing map load versus a row-level warning. This is the narrow source/UX choice requiring parent review; no redesign is proposed here.

Solution-list failure may leave readable maps, but it requires an independent visible nonfatal warning. Only a successful solution reload clears it; successful map loads must not.

## Scope and ownership

Proposed production ownership is the named parsers/readers, a smallest domain parse-error helper/type if needed, Map VM/view warning, and corresponding parser/reader/VM/render tests. Do not change auth/trust, paging retry/cycle policy, export, or Compare matching.

## Pre-mortem

1. HTML/garbage appears as empty: strict envelope validation.
2. A later bad page appears complete: fail the whole read.
3. Sparse legitimate gateway maps are rejected: retain optional-field semantics.
4. Solution failure is hidden by map success: independent warning lifecycle.
5. Diagnostics leak rows or cancellation is flattened: structural-only diagnostics and cancellation pass-through.

## Acceptance matrix

Cover valid empty and supported aliases; malformed first/later pages; mixed arrays; optional missing fields; normal virtual filtering; malformed nested JSON diagnostic; no partial success; visible solution warning; and no live calls.
